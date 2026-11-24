using System.Text;

namespace Nest.Security.Authentication;

internal sealed class AuthenticationService
{
    // Public synthetic fixture, not a real user's credential; never a fallback account.
    private const string DummyCredential = "$nest$pbkdf2-sha256$v1$600000$AAECAwQFBgcICQoLDA0ODw==$RKFiwVeyMhcIzDwIo+faCKUyEM8cRVu7JZEGBVPlEa8=";
    private readonly IAuthenticationStore store;
    private readonly IPasswordCryptography crypto;
    internal AuthenticationService(IAuthenticationStore store, IPasswordCryptography? crypto = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.crypto = crypto ?? PasswordCryptography.Shared;
    }
    internal async Task<AuthenticationResult> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        StoreOutcome? finalization = null;
        bool finalizationStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is null || string.IsNullOrEmpty(request.UserName) || request.Password is null)
                return await RefuseAsync(request?.Password, cancellationToken).ConfigureAwait(false);
            var snapshot = await store.FindAsync(request.UserName, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is null || snapshot.UserId <= 0 || snapshot.CredentialRevision < 0 || snapshot.AccountRevision < 0 ||
                snapshot.Status != StaffStatus.Active || snapshot.DeletedAt != null || !AccountRules.RoleAllowed(snapshot.Role) ||
                snapshot.Format != PasswordFormat.Pbkdf2Sha256V1 || snapshot.UserName is null)
                return await RefuseAsync(request.Password, cancellationToken).ConfigureAwait(false);
            var verified = await crypto.VerifyAsync(request.Password, snapshot.Credential, cancellationToken).ConfigureAwait(false);
            if (verified == PasswordVerificationResult.InvalidCredential)
                return await RefuseAsync(request.Password, cancellationToken).ConfigureAwait(false);
            if (verified != PasswordVerificationResult.Success && verified != PasswordVerificationResult.SuccessRehashNeeded)
                return new(AuthenticationOutcome.Rejected);
            if (verified == PasswordVerificationResult.SuccessRehashNeeded && !AccountRules.CanIncrement(snapshot.CredentialRevision))
                return new(AuthenticationOutcome.Rejected); // Cannot perform required rehash; no command admitted.
            PreparedCredential? rehash = verified == PasswordVerificationResult.SuccessRehashNeeded
                ? await crypto.PrepareAsync(request.Password, cancellationToken).ConfigureAwait(false) : null;
            cancellationToken.ThrowIfCancellationRequested();
            finalizationStarted = true;
            var result = await store.FinalizeAsync(snapshot.Stamp, rehash).ConfigureAwait(false);
            finalization = AccountRules.AfterCommand(result?.Outcome ?? StoreOutcome.Unknown);
            if (result?.User != null && AccountRules.NoChanges(finalization.Value))
                finalization = StoreOutcome.UncertainOutcome; // Contradictory refusal payload proves neither outcome.
            if (cancellationToken.IsCancellationRequested) return new(AuthenticationOutcome.Cancelled, FinalizationOutcome: finalization);
            if (AccountRules.Committed(finalization.Value))
            {
                if (result?.User is null || result.User.Id != snapshot.UserId || result.User.UserName != snapshot.UserName ||
                    result.User.Role != snapshot.Role || result.User.AccountRevision != snapshot.AccountRevision)
                    return new(AuthenticationOutcome.TechnicalFailure, FinalizationOutcome: finalization);
                // Commit was acknowledged: cleanup failure is a warning, never a rollback/retry.
                return new(finalization == StoreOutcome.Success ? AuthenticationOutcome.Success : AuthenticationOutcome.CommittedWithCleanupFailure,
                    result.User, finalization);
            }
            return new(Map(finalization.Value), FinalizationOutcome: finalization);
        }
        catch (OperationCanceledException)
        {
            finalization ??= finalizationStarted ? StoreOutcome.UncertainOutcome : null;
            return new(cancellationToken.IsCancellationRequested ? AuthenticationOutcome.Cancelled
                : finalizationStarted ? AuthenticationOutcome.UncertainOutcome : AuthenticationOutcome.TechnicalFailure,
                FinalizationOutcome: finalization);
        }
        catch (EncoderFallbackException) when (!finalizationStarted)
        {
            return new(AuthenticationOutcome.Rejected);
        }
        catch (Exception)
        {
            // Do not expose exceptions, snapshots, request values or driver diagnostics.
            return new(finalizationStarted ? AuthenticationOutcome.UncertainOutcome : AuthenticationOutcome.TechnicalFailure,
                FinalizationOutcome: finalizationStarted ? StoreOutcome.UncertainOutcome : null);
        }
    }
    private async Task<AuthenticationResult> RefuseAsync(string? password, CancellationToken cancellationToken)
    {
        // Same bounded worker pool as real verification; not a complete timing-side-channel defense.
        _ = await crypto.VerifyAsync(password ?? "synthetic-dummy-input", DummyCredential, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(AuthenticationOutcome.Rejected);
    }
    private static AuthenticationOutcome Map(StoreOutcome value) => value switch
    {
        StoreOutcome.ConcurrencyConflict => AuthenticationOutcome.ConcurrencyConflict,
        StoreOutcome.Rejected => AuthenticationOutcome.Rejected,
        StoreOutcome.CommitUncertain => AuthenticationOutcome.CommitUncertain,
        StoreOutcome.UncertainOutcome => AuthenticationOutcome.UncertainOutcome,
        _ => AuthenticationOutcome.TechnicalFailure
    };
}
