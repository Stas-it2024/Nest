using System.Text;

namespace Nest.Security.Authentication;

internal sealed class UserManagementService
{
    private readonly IUserManagementStore store;
    private readonly IPasswordCryptography crypto;
    internal UserManagementService(IUserManagementStore store, IPasswordCryptography? crypto = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.crypto = crypto ?? PasswordCryptography.Shared;
    }
    private static bool Valid(UserProfile? profile) => profile != null && profile.UserName != null && profile.FullName != null &&
        AccountRules.RoleAllowed(profile.Role) && AccountRules.StatusAllowed(profile.Status);
    private static bool Valid(AccountStamp stamp) => stamp.UserId > 0 && stamp.CredentialRevision >= 0 && stamp.AccountRevision >= 0;

    internal async Task<UserWriteResult> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Preserve the existing admin rule; it is not part of PasswordHasher or legacy migration.
            if (request is null || !Valid(request.Profile) || string.IsNullOrWhiteSpace(request.Password)) return new(StoreOutcome.Rejected);
            var credential = await crypto.PrepareAsync(request.Password, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await WriteAsync(() => store.CreateAsync(request.Profile, credential)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(cancellationToken.IsCancellationRequested ? StoreOutcome.Cancelled : StoreOutcome.TechnicalFailure); }
        catch (EncoderFallbackException) { return new(StoreOutcome.Rejected); }
        catch (Exception) { return new(StoreOutcome.TechnicalFailure); }
    }
    internal Task<UserWriteResult> UpdateProfileAsync(AccountStamp expected, UserProfile profile, CancellationToken cancellationToken = default) =>
        EditUserAsync(new(expected, profile, PasswordChange.KeepExisting()), cancellationToken);
    internal Task<UserWriteResult> ChangePasswordAsync(AccountStamp expected, string? password, CancellationToken cancellationToken = default) =>
        EditUserAsync(new(expected, null, PasswordChange.Replace(password)), cancellationToken);

    internal async Task<UserWriteResult> EditUserAsync(EditUserRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is null || !Valid(request.Expected) || request.PasswordChange is null ||
                (request.Profile != null && !Valid(request.Profile))) return new(StoreOutcome.Rejected);
            if (request.PasswordChange.Kind == PasswordChangeKind.KeepExisting)
            {
                if (request.Profile is null || !AccountRules.CanIncrement(request.Expected.AccountRevision)) return new(StoreOutcome.Rejected);
                cancellationToken.ThrowIfCancellationRequested();
                return await WriteAsync(() => store.UpdateProfileAsync(request.Expected, request.Profile)).ConfigureAwait(false);
            }
            if (request.PasswordChange.Kind != PasswordChangeKind.Replace || string.IsNullOrWhiteSpace(request.PasswordChange.Password))
                return new(StoreOutcome.Rejected);
            if (!AccountRules.CanIncrement(request.Expected.CredentialRevision) || !AccountRules.CanIncrement(request.Expected.AccountRevision))
                return new(StoreOutcome.Rejected);
            var credential = await crypto.PrepareAsync(request.PasswordChange.Password, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await WriteAsync(() => request.Profile is null
                ? store.ChangeCredentialAsync(request.Expected, credential)
                : store.UpdateProfileAndCredentialAsync(request.Expected, request.Profile, credential)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(cancellationToken.IsCancellationRequested ? StoreOutcome.Cancelled : StoreOutcome.TechnicalFailure); }
        catch (EncoderFallbackException) { return new(StoreOutcome.Rejected); }
        catch (Exception) { return new(StoreOutcome.TechnicalFailure); }
    }
    internal Task<UserWriteResult> SoftDeleteAsync(AccountStamp expected, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(new UserWriteResult(StoreOutcome.Cancelled));
        return Valid(expected) && AccountRules.CanIncrement(expected.AccountRevision) ? WriteAsync(() => store.SoftDeleteAsync(expected)) : Task.FromResult(new UserWriteResult(StoreOutcome.Rejected));
    }
    private static async Task<UserWriteResult> WriteAsync(Func<Task<UserWriteResult>> operation)
    {
        try
        {
            // Once admitted, do not abandon a write on UI cancellation or retry it.
            var result = await operation().ConfigureAwait(false);
            return new(AccountRules.AfterCommand(result?.Outcome ?? StoreOutcome.Unknown));
        }
        catch (Exception) { return new(StoreOutcome.UncertainOutcome); }
    }
}
