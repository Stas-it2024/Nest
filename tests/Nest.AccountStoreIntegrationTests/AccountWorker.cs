using System.Collections.Concurrent;
using Nest.Security;
using Nest.Security.Authentication;

internal sealed record WorkerResult(StoreOutcome Outcome, bool Valid = true, bool Authenticated = false)
{
    internal static WorkerResult Lost => new(StoreOutcome.UncertainOutcome, false);
    internal bool Reliable => Valid && Outcome is StoreOutcome.Success or StoreOutcome.ConcurrencyConflict or StoreOutcome.Rejected;
}
internal sealed class AccountWorker
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource go = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int aborted;
    internal ConcurrentBag<long> Connections { get; } = new();
    internal Task Ready => ready.Task;
    internal Task<WorkerResult> Completion { get; }
    private AccountWorker(Func<AccountWorker, Task<WorkerResult>> operation)
    {
        Completion = Task.Run(async () =>
        {
            try { return await operation(this); }
            catch
            {
                ready.TrySetException(new InvalidOperationException("Worker failed before readiness."));
                return WorkerResult.Lost; // No assertion of rollback after a lost/exceptional result.
            }
        });
    }
    private async Task BarrierAsync()
    {
        ready.TrySetResult(); await go.Task;
        AccountDatabaseGuard.Require(Volatile.Read(ref aborted) == 0);
    }
    internal void Go() => go.TrySetResult();
    internal void AbortBeforeGo() { Interlocked.Exchange(ref aborted, 1); go.TrySetResult(); }
    internal static AccountWorker Simulate(Func<Task<WorkerResult>> body)
    {
        AccountDatabaseGuard.Require(AccountDatabaseGuard.IsOffline);
        return new(async worker => { await worker.BarrierAsync(); return await body(); });
    }
    internal static AccountWorker Edit(AccountDatabaseGuard guard, AccountFixture fixture, IReadOnlySet<int> originals,
        string operation) => new(async worker =>
    {
        var snapshot = await fixture.ReadAsync(guard, originals);
        if (operation == "rehash")
            AccountDatabaseGuard.Require(await PasswordCryptography.Shared.VerifyAsync(AccountFixture.Password,
                snapshot.Credential, CancellationToken.None) == PasswordVerificationResult.SuccessRehashNeeded);
        var replacement = operation is "password" or "rehash" ?
            await PreparedCredential.CreateAsync(operation == "rehash" ? AccountFixture.Password : AccountFixture.ReplacementPassword) : null;
        await worker.BarrierAsync();
        var store = guard.Store(id => worker.Connections.Add(id));
        var stamp = fixture.Stamp(snapshot);
        var outcome = operation switch
        {
            "profile" => (await store.UpdateProfileAsync(stamp, fixture.Profile() with { Role = StaffRole.Admin })).Outcome,
            "password" => (await store.ChangeCredentialAsync(stamp, replacement!)).Outcome,
            "rehash" => (await store.FinalizeAsync(snapshot.Stamp, replacement)).Outcome,
            _ => throw new InvalidOperationException("Unsupported worker operation.")
        };
        return new(outcome);
    });
    internal static AccountWorker Login(AccountDatabaseGuard guard, AccountFixture fixture) => new(async worker =>
    {
        var crypto = new PausedVerification(worker);
        var service = new AuthenticationService(guard.Store(id => worker.Connections.Add(id)), crypto);
        var result = await service.AuthenticateAsync(new LoginRequest(fixture.Name, AccountFixture.Password));
        // In this worker a successful real verification was paused BEFORE finalization.
        return new(result.FinalizationOutcome ?? StoreOutcome.Unknown,
            result.FinalizationOutcome.HasValue, result.IsAuthenticated);
    });
    private sealed class PausedVerification(AccountWorker worker) : IPasswordCryptography
    {
        public async Task<PasswordVerificationResult> VerifyAsync(string password, string? credential, CancellationToken token)
        {
            var result = await PasswordCryptography.Shared.VerifyAsync(password, credential, token);
            AccountDatabaseGuard.Require(result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded);
            await worker.BarrierAsync();
            return result;
        }
        public Task<PreparedCredential> PrepareAsync(string password, CancellationToken token) =>
            PasswordCryptography.Shared.PrepareAsync(password, token);
    }
    internal static async Task<bool> FinishAsync(IEnumerable<AccountWorker> workers)
    {
        var all = workers.ToArray();
        foreach (var worker in all) worker.AbortBeforeGo();
        try { await Task.WhenAll(all.Select(x => x.Completion)).WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { return false; }
        return all.All(x => x.Completion.IsCompletedSuccessfully);
    }
}