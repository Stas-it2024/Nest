namespace Nest.Security.Authentication;

// Internal to the application: these are not a database/client authentication boundary.
internal enum PasswordFormat { LegacyPlaintext = 0, Pbkdf2Sha256V1 = 1, Disabled = 2 }
internal enum StaffRole { Unknown = 0, Admin = 1, Receptionist = 2 }
internal enum StaffStatus { Unknown = 0, Active = 1, Inactive = 2 }
internal enum PasswordChangeKind { Unknown = 0, KeepExisting = 1, Replace = 2 }
// Normative outcome semantics apply to the operation, not to whether a connection was disposed.
internal enum StoreOutcome
{
    Unknown = 0, // Invalid result: neither success nor absence of changes is established.
    Success = 1, // Operation completed; a transactional commit was acknowledged.
    ConcurrencyConflict = 2, // No changes from this operation; rollback confirmed if needed.
    Rejected = 3, // No changes from this operation; rejection is definite.
    CommitUncertain = 4, // Commit was attempted; its result is unknown.
    UncertainOutcome = 5, // Final state is unknown, including failed rollback.
    CommittedWithCleanupFailure = 6, // Commit acknowledged, subsequent cleanup failed.
    TechnicalFailure = 7, // Technical error with definite absence of changes.
    Cancelled = 8 // SERVICE ONLY: cancellation before admitting any store command.
}
internal enum AuthenticationOutcome
{
    Unknown = 0, Success = 1, Rejected = 2, ConcurrencyConflict = 3, CommitUncertain = 4,
    UncertainOutcome = 5, CommittedWithCleanupFailure = 6, TechnicalFailure = 7, Cancelled = 8
}
internal static class AccountRules
{
    internal const long InitialRevision = 1;
    internal static bool RoleAllowed(StaffRole role) => role is StaffRole.Admin or StaffRole.Receptionist;
    internal static bool StatusAllowed(StaffStatus status) => status is StaffStatus.Active or StaffStatus.Inactive;
    // Signed BIGINT range: 0..Int64.MaxValue. Zero permits an explicitly imported baseline.
    internal static bool RevisionValid(long revision) => revision >= 0;
    internal static bool CanIncrement(long revision) => revision >= 0 && revision < long.MaxValue;
    internal static bool TryNext(long revision, out long next)
    {
        next = revision;
        if (!CanIncrement(revision)) return false;
        next = revision + 1; return true;
    }
    internal static bool Committed(StoreOutcome outcome) => outcome is StoreOutcome.Success or StoreOutcome.CommittedWithCleanupFailure;
    internal static bool NoChanges(StoreOutcome outcome) => outcome is StoreOutcome.Rejected or StoreOutcome.ConcurrencyConflict or StoreOutcome.TechnicalFailure;
    internal static StoreOutcome AfterCommand(StoreOutcome outcome) => outcome switch
    {
        StoreOutcome.Success or StoreOutcome.ConcurrencyConflict or StoreOutcome.Rejected or StoreOutcome.CommitUncertain or
        StoreOutcome.UncertainOutcome or StoreOutcome.CommittedWithCleanupFailure or StoreOutcome.TechnicalFailure => outcome,
        _ => StoreOutcome.UncertainOutcome // Unknown, Cancelled and undefined values prove nothing after admission.
    };
}

internal sealed class LoginRequest(string? userName, string? password)
{
    internal string? UserName { get; } = userName;
    internal string? Password { get; } = password;
    public override string ToString() => "LoginRequest [redacted]";
}
internal sealed record UserProfile(string UserName, string FullName, StaffRole Role, StaffStatus Status);
internal sealed record AuthenticatedUser(int Id, string UserName, StaffRole Role, long AccountRevision);
internal readonly record struct AccountStamp(int UserId, long CredentialRevision, long AccountRevision);
internal readonly record struct AuthenticationStamp(int UserId, long CredentialRevision, long AccountRevision);

internal sealed class AuthenticationSnapshot(int userId, string userName, StaffRole role, StaffStatus status,
    DateTime? deletedAt, string? credential, PasswordFormat format, long credentialRevision, long accountRevision)
{
    internal int UserId { get; } = userId;
    internal string UserName { get; } = userName;
    internal StaffRole Role { get; } = role;
    internal StaffStatus Status { get; } = status;
    internal DateTime? DeletedAt { get; } = deletedAt;
    internal string? Credential { get; } = credential;
    internal PasswordFormat Format { get; } = format;
    internal long CredentialRevision { get; } = credentialRevision;
    internal long AccountRevision { get; } = accountRevision;
    internal AuthenticationStamp Stamp => new(UserId, CredentialRevision, AccountRevision);
    public override string ToString() => "AuthenticationSnapshot [redacted]";
}
internal sealed class PreparedCredential
{
    private PreparedCredential(string value) { Value = value; }
    // No encoded-input factory: normal callers cannot wrap an old work factor for writing.
    // The factory hashes the literal password using CURRENT policy, even if it looks like a credential.
    // The factory itself owns admission. Direct calls are bounded and never hash on the caller's UI thread.
    internal static Task<PreparedCredential> CreateAsync(string password, CancellationToken cancellationToken = default) =>
        PasswordWorkLimiter.RunAsync(() => new PreparedCredential(PasswordHasher.HashPassword(password)),
            cancellationToken, PasswordWorkKind.Hash);
    internal string Value { get; }
    internal PasswordFormat Format => PasswordFormat.Pbkdf2Sha256V1;
    public override string ToString() => "PreparedCredential [redacted]";
}
internal sealed class PasswordChange
{
    private PasswordChange(PasswordChangeKind kind, string? password) { Kind = kind; Password = password; }
    internal PasswordChangeKind Kind { get; }
    internal string? Password { get; }
    internal static PasswordChange KeepExisting() => new(PasswordChangeKind.KeepExisting, null);
    internal static PasswordChange Replace(string? password) => new(PasswordChangeKind.Replace, password);
    public override string ToString() => "PasswordChange [redacted]";
}
internal sealed class CreateUserRequest(UserProfile profile, string? password)
{
    internal UserProfile Profile { get; } = profile;
    internal string? Password { get; } = password;
    public override string ToString() => "CreateUserRequest [redacted]";
}
internal sealed class EditUserRequest(AccountStamp expected, UserProfile? profile, PasswordChange passwordChange)
{
    internal AccountStamp Expected { get; } = expected;
    internal UserProfile? Profile { get; } = profile;
    internal PasswordChange PasswordChange { get; } = passwordChange;
    public override string ToString() => "EditUserRequest [redacted]";
}
internal sealed record AuthenticationFinalizeResult(StoreOutcome Outcome = StoreOutcome.Unknown, AuthenticatedUser? User = null);
internal sealed record UserWriteResult(StoreOutcome Outcome = StoreOutcome.Unknown)
{
    internal bool IsCommitted => AccountRules.Committed(Outcome);
}
internal sealed record AuthenticationResult(AuthenticationOutcome Outcome = AuthenticationOutcome.Unknown, AuthenticatedUser? User = null,
    StoreOutcome? FinalizationOutcome = null)
{
    internal bool IsAuthenticated => User != null && User.Id > 0 && User.UserName != null && AccountRules.RoleAllowed(User.Role) &&
        AccountRules.RevisionValid(User.AccountRevision) &&
        ((Outcome == AuthenticationOutcome.Success && FinalizationOutcome == StoreOutcome.Success) ||
         (Outcome == AuthenticationOutcome.CommittedWithCleanupFailure && FinalizationOutcome == StoreOutcome.CommittedWithCleanupFailure));
    // All refusals, including account state/format/conflicts, share the same external message.
    internal string Message => IsAuthenticated ? "Anmeldung erfolgreich." : "Die Anmeldung konnte nicht durchgeführt werden.";
}
