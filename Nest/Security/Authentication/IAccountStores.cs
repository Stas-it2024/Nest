namespace Nest.Security.Authentication;

// Normative SQL contract (implementation belongs to a later stage):
// * Reads have no writes; a read failure may throw, never exposing credential/connection details.
// * Finalization, even without rehash, is one short ReadCommitted transaction with SELECT FOR UPDATE.
// * Compare both revisions and current identity/state under the SAME lock; never PBKDF2 under a lock.
// * Writes are parameterized conditional updates; exactly one row required. Zero is conflict/missing,
//   more than one is an invariant failure: rollback, do not report success.
// * StoreOutcome definitions are mandatory. Unknown/undefined/Cancelled are invalid store outcomes.
// * Failure before SQL writes, or with acknowledged rollback, may be TechnicalFailure/Rejected.
//   Failed rollback is UncertainOutcome; failed/unacknowledged Commit is CommitUncertain.
// * Acknowledged Commit survives every later error: return Success/CommittedWithCleanupFailure.
//   Dispose success does NOT prove rollback; Dispose failure does NOT undo an acknowledged Commit.
// * No retries. Once admitted, observe the actual outcome (bounded driver/command timeouts).
// * Revisions are signed BIGINT 0..Int64.MaxValue; new accounts start at 1 for BOTH.
//   Zero is only an explicit import baseline. Check ALL required increments BEFORE any mutation.
//   Exhaustion => Rejected with no changes, never wrap/saturate/reset. Reads/nonincrementing
//   finalization remain allowed at MaxValue. A rehash needs only credential_revision capacity.
// * All writers must maintain revisions; legacy/unversioned writers must be excluded before rollout.
internal interface IAuthenticationStore
{
    // Detached snapshot; lookup follows the separately agreed username matching policy.
    // No connection or lock survives the read. Snapshot never goes to UI/session/logging.
    Task<AuthenticationSnapshot?> FindAsync(string userName, CancellationToken cancellationToken);

    // Recheck ID, BOTH revisions, Active/not-deleted state, allowed role and PBKDF2 format.
    // No rehash: no row changes, but transaction completion must still be acknowledged.
    // Rehash: conditional update, credential_revision + 1 ONLY. Conflict => no partial changes.
    // Success/cleanup-warning requires the authoritative safe user; refusals must carry no user.
    // No cancellation token after admission: UI cancellation cannot assert rollback.
    Task<AuthenticationFinalizeResult> FinalizeAsync(AuthenticationStamp expected, PreparedCredential? rehash);
}
internal interface IUserManagementStore
{
    // Store receives prepared credentials only; never hash/verify passwords in a transaction.
    // Validate profile/credential/format/stamp and reject duplicate names/invalid rows safely.
    // Compare BOTH expected revisions and nondeleted state atomically for EVERY edit/delete.
    // Profile/delete: account_revision + 1. Password: BOTH + 1. Combined edit: each + 1 once.
    // Any conflict/exhaustion/refusal changes neither profile nor credential nor revisions.
    Task<UserWriteResult> CreateAsync(UserProfile profile, PreparedCredential credential);
    Task<UserWriteResult> UpdateProfileAsync(AccountStamp expected, UserProfile profile);
    Task<UserWriteResult> ChangeCredentialAsync(AccountStamp expected, PreparedCredential credential);
    Task<UserWriteResult> UpdateProfileAndCredentialAsync(AccountStamp expected, UserProfile profile, PreparedCredential credential);
    Task<UserWriteResult> SoftDeleteAsync(AccountStamp expected);
}
