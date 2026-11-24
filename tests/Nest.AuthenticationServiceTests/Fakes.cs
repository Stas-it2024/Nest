using Nest.Security;
using Nest.Security.Authentication;

internal sealed class FakeAccountStore : IAuthenticationStore, IUserManagementStore
{
    private readonly object gate = new();
    internal int Id = 1;
    internal string Name = "offline-user", FullName = "Synthetic User";
    internal StaffRole Role = StaffRole.Admin;
    internal StaffStatus Status = StaffStatus.Active;
    internal DateTime? DeletedAt;
    internal string? Credential = Fixtures.Current;
    internal PasswordFormat Format = PasswordFormat.Pbkdf2Sha256V1;
    internal long CredentialRevision = 1, AccountRevision = 1;
    internal bool Missing, ThrowRead, ThrowFinalize, ThrowWrite, ApplyUncertain, OmitFinalUser, NullFinalize, NullWrite, FailurePayload;
    internal AuthenticatedUser? ReturnedUserOverride;
    internal int Finds, Finalizes, Creates, Profiles, Passwords, Combined, Deletes;
    internal StoreOutcome? ForcedOutcome;
    internal Func<Task>? BeforeFinalize, BeforeWrite;
    internal Action? AfterFinalize;
    internal PreparedCredential? LastPrepared;
    internal AccountStamp Stamp { get { lock (gate) return new(Id, CredentialRevision, AccountRevision); } }
    internal void Mutate(Action<FakeAccountStore> action) { lock (gate) action(this); }
    public Task<AuthenticationSnapshot?> FindAsync(string userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref Finds);
        if (ThrowRead) throw new InvalidOperationException("synthetic sensitive transport failure");
        lock (gate)
            // Deliberately ordinal fake lookup, not an emulation of MariaDB collation.
            return Task.FromResult(Missing || !string.Equals(userName, Name, StringComparison.Ordinal) ? null : new AuthenticationSnapshot(Id, Name, Role, Status, DeletedAt, Credential,
                Format, CredentialRevision, AccountRevision));
    }
    public async Task<AuthenticationFinalizeResult> FinalizeAsync(AuthenticationStamp expected, PreparedCredential? rehash)
    {
        Interlocked.Increment(ref Finalizes);
        if (BeforeFinalize != null) await BeforeFinalize();
        if (ThrowFinalize) throw new OperationCanceledException("synthetic ambiguous finalization");
        if (NullFinalize) return null!;
        AuthenticationFinalizeResult result;
        lock (gate)
        {
            if (ForcedOutcome is { } forced && !AccountRules.Committed(forced) && !(ApplyUncertain && forced is StoreOutcome.CommitUncertain or StoreOutcome.UncertainOutcome))
                result = new(forced, FailurePayload ? new(Id, Name, Role, AccountRevision) : null);
            else if (Missing || DeletedAt != null || Status != StaffStatus.Active || !AccountRules.RoleAllowed(Role) || Format != PasswordFormat.Pbkdf2Sha256V1)
                result = new(StoreOutcome.Rejected);
            else if (expected != new AuthenticationStamp(Id, CredentialRevision, AccountRevision))
                result = new(StoreOutcome.ConcurrencyConflict);
            else if (rehash != null && !AccountRules.CanIncrement(CredentialRevision))
                result = new(StoreOutcome.Rejected);
            else
            {
                if (rehash != null)
                {
                    if (!AccountRules.TryNext(CredentialRevision, out var next)) throw new InvalidOperationException("Exhausted revision.");
                    Credential = rehash.Value; Format = rehash.Format; CredentialRevision = next; LastPrepared = rehash;
                }
                result = new(ForcedOutcome ?? StoreOutcome.Success, OmitFinalUser ? null : ReturnedUserOverride ?? new(Id, Name, Role, AccountRevision));
            }
        }
        AfterFinalize?.Invoke(); return result;
    }
    private async Task<UserWriteResult> Write(AccountStamp? expected, Action apply, bool changesCredential = false)
    {
        if (BeforeWrite != null) await BeforeWrite();
        if (ThrowWrite) throw new OperationCanceledException("synthetic ambiguous write");
        if (NullWrite) return null!;
        lock (gate)
        {
            if (ForcedOutcome is { } forced && !AccountRules.Committed(forced) && !(ApplyUncertain && forced is StoreOutcome.CommitUncertain or StoreOutcome.UncertainOutcome))
                return new(forced);
            if (expected != null && (Missing || DeletedAt != null || expected != new AccountStamp(Id, CredentialRevision, AccountRevision)))
                return new(StoreOutcome.ConcurrencyConflict);
            long nextAccount = AccountRevision, nextCredential = CredentialRevision;
            if (expected != null && (!AccountRules.TryNext(AccountRevision, out nextAccount) ||
                (changesCredential && !AccountRules.TryNext(CredentialRevision, out nextCredential))))
                return new(StoreOutcome.Rejected);
            apply();
            if (expected != null) { AccountRevision = nextAccount; CredentialRevision = nextCredential; }
            return new(ForcedOutcome ?? StoreOutcome.Success);
        }
    }
    public Task<UserWriteResult> CreateAsync(UserProfile profile, PreparedCredential credential)
    {
        Interlocked.Increment(ref Creates);
        return Write(null, () => { ApplyProfile(profile); ApplyCredential(credential); Id = 2; CredentialRevision = AccountRevision = AccountRules.InitialRevision; });
    }
    public Task<UserWriteResult> UpdateProfileAsync(AccountStamp expected, UserProfile profile)
    {
        Interlocked.Increment(ref Profiles);
        return Write(expected, () => { ApplyProfile(profile); });
    }
    public Task<UserWriteResult> ChangeCredentialAsync(AccountStamp expected, PreparedCredential credential)
    {
        Interlocked.Increment(ref Passwords);
        return Write(expected, () => { ApplyCredential(credential); }, changesCredential: true);
    }
    public Task<UserWriteResult> UpdateProfileAndCredentialAsync(AccountStamp expected, UserProfile profile, PreparedCredential credential)
    {
        Interlocked.Increment(ref Combined);
        return Write(expected, () => { ApplyProfile(profile); ApplyCredential(credential); }, changesCredential: true);
    }
    public Task<UserWriteResult> SoftDeleteAsync(AccountStamp expected)
    {
        Interlocked.Increment(ref Deletes);
        return Write(expected, () => { DeletedAt = new DateTime(2026, 1, 1); });
    }
    private void ApplyProfile(UserProfile profile) { Name = profile.UserName; FullName = profile.FullName; Role = profile.Role; Status = profile.Status; }
    private void ApplyCredential(PreparedCredential credential) { Credential = credential.Value; Format = credential.Format; LastPrepared = credential; }
}
internal sealed class ControlledCryptography : IPasswordCryptography
{
    internal Action? BeforeVerify;
    internal bool Fail;
    internal int Verifications, Preparations;
    public Task<PasswordVerificationResult> VerifyAsync(string password, string? credential, CancellationToken cancellationToken) =>
        PasswordWorkLimiter.RunAsync(() =>
        {
            Interlocked.Increment(ref Verifications); BeforeVerify?.Invoke();
            if (Fail) throw new InvalidOperationException("synthetic cryptographic failure");
            return PasswordHasher.VerifyPassword(password, credential);
        }, cancellationToken);
    public async Task<PreparedCredential> PrepareAsync(string password, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Preparations);
        if (Fail) throw new InvalidOperationException("synthetic cryptographic failure");
        // No outer limiter here: the real factory owns it, just as in production.
        return await PreparedCredential.CreateAsync(password, cancellationToken).ConfigureAwait(false);
    }
}
internal static class Fixtures
{
    internal const string Password = "Синтетический пароль Äß Ω";
    internal const string Current = "$nest$pbkdf2-sha256$v1$600000$AAECAwQFBgcICQoLDA0ODw==$RKFiwVeyMhcIzDwIo+faCKUyEM8cRVu7JZEGBVPlEa8=";
    internal const string Old = "$nest$pbkdf2-sha256$v1$100000$AAECAwQFBgcICQoLDA0ODw==$NBDXUqEGGnZ6a5ImW6afSFoYWiTrTz63X09R1MxNgO0=";
    internal const string Strong = "$nest$pbkdf2-sha256$v1$1200000$AAECAwQFBgcICQoLDA0ODw==$EZzBMuqdymsJ9rzieLjHuQRTNhNa458uspRbqzfa5RA=";
    internal static readonly UserProfile Profile = new("offline-user", "Synthetic Edited", StaffRole.Receptionist, StaffStatus.Active);
    internal static LoginRequest Login => new("offline-user", Password);
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(20));
    internal static void Block(ManualResetEventSlim gate)
    { if (!gate.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Synthetic coordination failed."); }
}
