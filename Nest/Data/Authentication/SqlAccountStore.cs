using System.Buffers;
using System.Data;
using System.Text;
using MySql.Data.MySqlClient;
using Nest.Security;
using Nest.Security.Authentication;

namespace Nest.Data.Authentication;

// Deliberately not registered in the application's composition root.
internal sealed class SqlAccountStore : IAuthenticationStore, IUserManagementStore
{
    // Lookup is not storage: retain PAD SPACE/case/accent matching for bounded input.
    internal const int MaximumLookupCharacters = 1024;
    private static async Task<T> Schedule<T>(Func<T> work, Func<T> busy, CancellationToken cancellationToken = default)
    {
        using var admission = await AccountSqlAdmission.AcquireAsync(cancellationToken).ConfigureAwait(false);
        if (admission == null) return busy();
        cancellationToken.ThrowIfCancellationRequested();
        // Admission spans real execution AND cleanup; cancellation never releases a live worker.
        return await Task.Run(work, cancellationToken).ConfigureAwait(false);
    }
    private const string Columns = "id, benutzername, vollstaendiger_name, rolle, status, geloescht_am, passwort, password_format, credential_revision, account_revision";
    private readonly Func<AccountStoreSession> open;
    internal SqlAccountStore() : this(AccountStoreSession.OpenDefault) { }
    internal SqlAccountStore(Func<AccountStoreSession> open) => this.open = open ?? throw new ArgumentNullException(nameof(open));
    public Task<AuthenticationSnapshot?> FindAsync(string userName, CancellationToken cancellationToken) => Schedule(() =>
    {
        AccountStoreSession? session = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Fits(userName, MaximumLookupCharacters)) throw new ArgumentException("Account lookup input is invalid.");
            session = open(); cancellationToken.ThrowIfCancellationRequested();
            // Leave username comparison to the existing column collation. Never Trim/normalize.
            var rows = session.Read("SELECT " + Columns + " FROM benutzer WHERE benutzername=@name", ("@name", DbType.String, userName));
            cancellationToken.ThrowIfCancellationRequested();
            if (rows.Count > 1) throw new InvalidOperationException();
            return rows.Count == 0 ? null : Snapshot(rows[0]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw new InvalidOperationException("Account lookup could not be completed."); }
        finally { if (session?.Close() == true) throw new InvalidOperationException("Account lookup cleanup failed."); }
    }, () => throw new InvalidOperationException("Account store is busy."), cancellationToken);

    public Task<AuthenticationFinalizeResult> FinalizeAsync(AuthenticationStamp expected, PreparedCredential? rehash) => Schedule(() =>
    {
        if (!Valid(expected.UserId, expected.CredentialRevision, expected.AccountRevision) || (rehash != null && !Valid(rehash))) return new AuthenticationFinalizeResult(StoreOutcome.Rejected);
        var result = Write(session =>
        {
            var row = Locked(session, expected.UserId);
            if (row == null || !Matches(row, expected.CredentialRevision, expected.AccountRevision) || row.DeletedAt != null)
                return (StoreOutcome.ConcurrencyConflict, null);
            if (row.Status != StaffStatus.Active || !AccountRules.RoleAllowed(row.Role) || row.Format != PasswordFormat.Pbkdf2Sha256V1)
                return (StoreOutcome.Rejected, null);
            if (rehash != null)
            {
                if (!AccountRules.TryNext(row.CredentialRevision, out var next)) return (StoreOutcome.Rejected, null);
                var args = Stamp(expected.UserId, expected.CredentialRevision, expected.AccountRevision).Concat(new[] {
                    Arg("@credential", DbType.String, rehash.Value), Arg("@format", DbType.Byte, (byte)1), Arg("@nextCredential", DbType.Int64, next) }).ToArray();
                var outcome = Rows(session.Execute("UPDATE benutzer SET passwort=@credential, password_format=@format, credential_revision=@nextCredential WHERE id=@id AND credential_revision=@expectedCredential AND account_revision=@expectedAccount AND geloescht_am IS NULL AND status='Aktiv' AND password_format=1", args));
                if (outcome != StoreOutcome.Success) return (outcome, null);
            }
            return (StoreOutcome.Success, new AuthenticatedUser(row.UserId, row.UserName, row.Role, row.AccountRevision));
        });
        return new AuthenticationFinalizeResult(result.Outcome, result.User);
    }, () => new AuthenticationFinalizeResult(StoreOutcome.TechnicalFailure));
    public Task<UserWriteResult> CreateAsync(UserProfile profile, PreparedCredential credential) => Schedule(() =>
    {
        if (!Valid(profile) || !Valid(credential)) return new UserWriteResult(StoreOutcome.Rejected);
        var result = Write(session => (Rows(session.Execute(
            "INSERT INTO benutzer (benutzername, vollstaendiger_name, rolle, status, passwort, password_format, credential_revision, account_revision, hinzugefuegt_am, geloescht_am) VALUES (@name, @fullName, @role, @status, @credential, @format, @initialCredential, @initialAccount, CURRENT_TIMESTAMP, NULL)",
            Profile(profile).Concat(new[] { Arg("@credential", DbType.String, credential.Value), Arg("@format", DbType.Byte, (byte)1),
                Arg("@initialCredential", DbType.Int64, AccountRules.InitialRevision), Arg("@initialAccount", DbType.Int64, AccountRules.InitialRevision) }).ToArray())), null));
        return new UserWriteResult(result.Outcome);
    }, () => new UserWriteResult(StoreOutcome.TechnicalFailure));
    public Task<UserWriteResult> UpdateProfileAsync(AccountStamp expected, UserProfile profile) => Edit(expected, profile, null, false);
    public Task<UserWriteResult> ChangeCredentialAsync(AccountStamp expected, PreparedCredential credential) => credential == null ? Task.FromResult(new UserWriteResult(StoreOutcome.Rejected)) : Edit(expected, null, credential, false);
    public Task<UserWriteResult> UpdateProfileAndCredentialAsync(AccountStamp expected, UserProfile profile, PreparedCredential credential) =>
        profile == null || credential == null ? Task.FromResult(new UserWriteResult(StoreOutcome.Rejected)) : Edit(expected, profile, credential, false);
    public Task<UserWriteResult> SoftDeleteAsync(AccountStamp expected) => Edit(expected, null, null, true);
    private Task<UserWriteResult> Edit(AccountStamp expected, UserProfile? profile, PreparedCredential? credential, bool delete) => Schedule(() =>
    {
        if (!Valid(expected.UserId, expected.CredentialRevision, expected.AccountRevision) || (!delete && profile == null && credential == null) ||
            (profile != null && !Valid(profile)) || (credential != null && !Valid(credential))) return new UserWriteResult(StoreOutcome.Rejected);
        var result = Write(session =>
        {
            var row = Locked(session, expected.UserId);
            if (row == null || row.DeletedAt != null || !Matches(row, expected.CredentialRevision, expected.AccountRevision)) return (StoreOutcome.ConcurrencyConflict, null);
            if (!AccountRules.TryNext(row.AccountRevision, out var account) ||
                (credential != null && !AccountRules.CanIncrement(row.CredentialRevision))) return (StoreOutcome.Rejected, null);
            var args = Stamp(expected.UserId, expected.CredentialRevision, expected.AccountRevision).ToList();
            args.Add(Arg("@nextAccount", DbType.Int64, account));
            var changes = new List<string> { "account_revision=@nextAccount" };
            if (profile != null)
            {
                changes.AddRange(new[] { "benutzername=@name", "vollstaendiger_name=@fullName", "rolle=@role", "status=@status" }); args.AddRange(Profile(profile));
            }
            if (credential != null)
            {
                changes.AddRange(new[] { "passwort=@credential", "password_format=@format", "credential_revision=@nextCredential" });
                args.AddRange(new[] { Arg("@credential", DbType.String, credential.Value), Arg("@format", DbType.Byte, (byte)1), Arg("@nextCredential", DbType.Int64, checked(row.CredentialRevision + 1)) });
            }
            if (delete) changes.Add("geloescht_am=CURRENT_TIMESTAMP");
            return (Rows(session.Execute("UPDATE benutzer SET " + string.Join(", ", changes) + " WHERE id=@id AND credential_revision=@expectedCredential AND account_revision=@expectedAccount AND geloescht_am IS NULL", args.ToArray())), null);
        });
        return new UserWriteResult(result.Outcome);
    }, () => new UserWriteResult(StoreOutcome.TechnicalFailure));
    private (StoreOutcome Outcome, AuthenticatedUser? User) Write(Func<AccountStoreSession, (StoreOutcome Outcome, AuthenticatedUser? User)> work)
    {
        AccountStoreSession? session = null;
        var outcome = StoreOutcome.TechnicalFailure; AuthenticatedUser? user = null;
        var commitAttempted = false; var committed = false; var finished = false;
        try
        {
            session = open(); session.Begin();
            (outcome, user) = work(session);
            if (outcome == StoreOutcome.Success)
            {
                commitAttempted = true; session.Commit(); committed = true; finished = true;
            }
            // All refusals leave through the same acknowledged-rollback path below.
        }
        catch (MySqlException ex) when (!commitAttempted && ex.Number == 1062) { outcome = StoreOutcome.Rejected; }
        catch { outcome = commitAttempted ? StoreOutcome.CommitUncertain : StoreOutcome.TechnicalFailure; }
        finally
        {
            if (session != null)
            {
                if (!finished && session.Transaction != null)
                {
                    try { session.Rollback(); }
                    catch { if (!commitAttempted) outcome = StoreOutcome.UncertainOutcome; session.Unusable = true; }
                }
                if (commitAttempted && !committed) session.Unusable = true;
                var cleanupFailed = !session.DisposeTransaction();
                cleanupFailed |= session.Close();
                if (committed && cleanupFailed) outcome = StoreOutcome.CommittedWithCleanupFailure;
            }
        }
        return (outcome, AccountRules.Committed(outcome) ? user : null);
    }
    private static StoreOutcome Rows(int count) => count switch
    { 1 => StoreOutcome.Success, 0 => StoreOutcome.ConcurrencyConflict, _ => throw new InvalidOperationException("Account write cardinality is invalid.") };
    private static AuthenticationSnapshot? Locked(AccountStoreSession session, int id)
    {
        var rows = session.Read("SELECT " + Columns + " FROM benutzer WHERE id=@id FOR UPDATE", Arg("@id", DbType.Int32, id));
        if (rows.Count > 1) throw new InvalidOperationException(); return rows.Count == 0 ? null : Snapshot(rows[0]);
    }
    private static AuthenticationSnapshot Snapshot(Dictionary<string, object?> r)
    {
        var id = checked((int)AccountStoreSchemaGuard.Number(r, "id"));
        var cr = AccountStoreSchemaGuard.Number(r, "credential_revision"); var ar = AccountStoreSchemaGuard.Number(r, "account_revision");
        var name = AccountStoreSchemaGuard.Text(r, "benutzername"); var fullName = AccountStoreSchemaGuard.Text(r, "vollstaendiger_name");
        var role = AccountStoreSchemaGuard.Text(r, "rolle") switch { "Admin" => StaffRole.Admin, "Rezeptionist" => StaffRole.Receptionist, _ => StaffRole.Unknown };
        var status = AccountStoreSchemaGuard.Text(r, "status") switch { "Aktiv" => StaffStatus.Active, "Inaktiv" => StaffStatus.Inactive, _ => StaffStatus.Unknown };
        var format = AccountStoreSchemaGuard.Number(r, "password_format"); var value = AccountStoreSchemaGuard.Text(r, "passwort");
        if (!Valid(id, cr, ar) || !Fits(name, 50) || !Fits(fullName, 100) || !AccountRules.RoleAllowed(role) || !AccountRules.StatusAllowed(status) ||
            format is < 0 or > 2 || value == null || !Fits(value, 255) || (r["geloescht_am"] is not null and not DateTime)) throw new InvalidOperationException();
        return new(id, name!, role, status, (DateTime?)r["geloescht_am"], value, (PasswordFormat)format, cr, ar);
    }
    private static bool Matches(AuthenticationSnapshot row, long cr, long ar) => row.CredentialRevision == cr && row.AccountRevision == ar;
    private static bool Valid(int id, long cr, long ar) => id > 0 && cr >= 0 && ar >= 0;
    private static bool Valid(UserProfile? p) => p != null && Fits(p.UserName, 50) && Fits(p.FullName, 100) && AccountRules.RoleAllowed(p.Role) && AccountRules.StatusAllowed(p.Status);
    private static bool Valid(PreparedCredential? c)
    {
        if (c == null || c.Format != PasswordFormat.Pbkdf2Sha256V1) return false;
        try { return !PasswordHasher.NeedsRehash(c.Value); } catch (FormatException) { return false; }
    }
    private static bool Fits(string? value, int limit)
    {
        if (value == null) return false;
        var text = value.AsSpan(); var count = 0;
        while (!text.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(text, out _, out var consumed) != OperationStatus.Done || ++count > limit) return false;
            text = text[consumed..];
        }
        return true;
    }
    private static (string Name, DbType Type, object? Value) Arg(string name, DbType type, object? value) => (name, type, value);
    private static (string Name, DbType Type, object? Value)[] Stamp(int id, long cr, long ar) => new[] { Arg("@id", DbType.Int32, id), Arg("@expectedCredential", DbType.Int64, cr), Arg("@expectedAccount", DbType.Int64, ar) };
    private static (string Name, DbType Type, object? Value)[] Profile(UserProfile p) => new[] { Arg("@name", DbType.String, p.UserName), Arg("@fullName", DbType.String, p.FullName),
        Arg("@role", DbType.String, p.Role switch { StaffRole.Admin => "Admin", StaffRole.Receptionist => "Rezeptionist", _ => throw new InvalidOperationException() }),
        Arg("@status", DbType.String, p.Status switch { StaffStatus.Active => "Aktiv", StaffStatus.Inactive => "Inaktiv", _ => throw new InvalidOperationException() }) };
}

// Separate from PasswordWorkLimiter. Contains counts/signals only, never request data.
internal static class AccountSqlAdmission
{
    internal const int ActiveLimit = 4, WaitingLimit = 16;
    private static readonly object Gate = new();
    private static readonly LinkedList<Waiter> Queue = new();
    private static int active;
    internal static (int Active, int Waiting) Snapshot { get { lock (Gate) return (active, Queue.Count); } }
    internal sealed class Lease : IDisposable
    {
        private int released;
        internal Lease(object authority)
        { if (!ReferenceEquals(authority, Gate)) throw new InvalidOperationException("SQL admission lease cannot be fabricated."); }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            lock (Gate)
            {
                active--;
                if (Queue.First is { } node)
                {
                    Queue.RemoveFirst(); node.Value.Node = null; active++;
                    node.Value.Completion.TrySetResult(new Lease(Gate));
                }
            }
        }
    }
    private sealed class Waiter
    {
        internal readonly TaskCompletionSource<Lease?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LinkedListNode<Waiter>? Node;
    }
    internal static ValueTask<Lease?> AcquireAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Waiter waiter;
        lock (Gate)
        {
            if (active < ActiveLimit) { active++; return ValueTask.FromResult<Lease?>(new Lease(Gate)); }
            if (Queue.Count >= WaitingLimit) return ValueTask.FromResult<Lease?>(null);
            waiter = new Waiter(); waiter.Node = Queue.AddLast(waiter);
        }
        return WaitAsync(waiter, token);
    }
    private static async ValueTask<Lease?> WaitAsync(Waiter waiter, CancellationToken token)
    {
        using var registration = token.Register(() =>
        {
            lock (Gate)
            {
                if (waiter.Node == null) return; // Already admitted; the caller observes cancellation and releases its lease.
                Queue.Remove(waiter.Node); waiter.Node = null;
                waiter.Completion.TrySetCanceled(token);
            }
        });
        return await waiter.Completion.Task.ConfigureAwait(false);
    }
}
