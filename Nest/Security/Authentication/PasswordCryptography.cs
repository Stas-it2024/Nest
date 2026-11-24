namespace Nest.Security.Authentication;

internal interface IPasswordCryptography
{
    Task<PasswordVerificationResult> VerifyAsync(string password, string? credential, CancellationToken cancellationToken);
    Task<PreparedCredential> PrepareAsync(string password, CancellationToken cancellationToken);
}
internal enum PasswordWorkKind { Other, Verification, Hash }
internal readonly record struct PasswordWorkObservation(PasswordWorkKind Kind, bool Started);
internal static class PasswordWorkLimiter
{
    internal const int Limit = 2;
    private static readonly SemaphoreSlim Gate = new(Limit, Limit);
    // Internal observation only, not a scheduler/override. No request, password or credential exposed.
    // Test observers are nonthrowing counters; they cannot acquire/release/bypass the private gate.
    internal static event Action<PasswordWorkObservation>? WorkObserved;
    internal static async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken,
        PasswordWorkKind kind = PasswordWorkKind.Other)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await Task.Run(() =>
            {
                var observer = WorkObserved;
                try { observer?.Invoke(new(kind, true)); return operation(); }
                finally { observer?.Invoke(new(kind, false)); }
            }, CancellationToken.None).ConfigureAwait(false);
            // The worker has actually ended before cancellation can release its slot.
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { Gate.Release(); }
    }
}
internal sealed class PasswordCryptography : IPasswordCryptography
{
    internal static PasswordCryptography Shared { get; } = new();
    public Task<PasswordVerificationResult> VerifyAsync(string password, string? credential, CancellationToken cancellationToken) =>
        PasswordWorkLimiter.RunAsync(() => PasswordHasher.VerifyPassword(password, credential), cancellationToken, PasswordWorkKind.Verification);
    public Task<PreparedCredential> PrepareAsync(string password, CancellationToken cancellationToken) =>
        PreparedCredential.CreateAsync(password, cancellationToken); // Factory owns the single slot; never nest admission.
}
