using System.Data;
using Nest.Data.Authentication;
using Nest.Security.Authentication;

internal sealed class AccountFixture
{
    internal static readonly string Password = "Synthetic Account Test! Кириллица Äß 42";
    internal static readonly string ReplacementPassword = "Synthetic Replacement! Ω 73";
    internal Guid Run { get; }
    internal Guid Token { get; } = Guid.NewGuid();
    internal string Marker => "NestAccountIT/" + Run.ToString("N") + "/" + Token.ToString("N");
    internal string Name { get; private set; }
    internal string FullName { get; private set; }
    internal int Id { get; private set; }
    internal bool Created { get; private set; }
    internal bool Verified { get; set; }
    internal bool ParticipantsFinished { get; set; } = true;
    internal bool Reliable { get; set; } = true;
    internal bool Cleaned { get; private set; }
    internal AccountStamp Stamp(AuthenticationSnapshot s) => new(s.UserId, s.CredentialRevision, s.AccountRevision);
    internal UserProfile Profile(StaffStatus status = StaffStatus.Active) =>
        new(Name, FullName, StaffRole.Receptionist, status);
    internal AccountFixture(Guid run)
    {
        AccountDatabaseGuard.Require(run != Guid.Empty);
        Run = run; Name = "nit_" + Token.ToString("N") + "_Ä";
        FullName = Marker;
    }
    public override string ToString() => $"Fixture run={Run:N} token={Token:N} id={Id}";
    internal static bool Owns(Guid run, Guid token, int id, string name, string fullName, IReadOnlySet<int> originals) =>
        run != Guid.Empty && token != Guid.Empty && id > 0 && !originals.Contains(id) &&
        name == "nit_" + token.ToString("N") + "_Ä" &&
        (fullName == "NestAccountIT/" + run.ToString("N") + "/" + token.ToString("N") ||
         fullName == "NestAccountIT/" + run.ToString("N") + "/" + token.ToString("N") + " Кириллица Äß Ω");

    internal async Task CreateAsync(AccountDatabaseGuard guard, IReadOnlySet<int> originals)
    {
        AccountDatabaseGuard.Require(!Created && Id == 0);
        var credential = await PreparedCredential.CreateAsync(Password);
        // Preflight name collision check returns only IDs; it never reads a foreign credential.
        var s = guard.Open();
        try { AccountDatabaseGuard.Require(s.Read("SELECT id FROM benutzer WHERE benutzername=@name",
            ("@name", DbType.String, Name)).Count == 0); }
        finally { AccountDatabaseGuard.Require(!s.Close()); }
        var result = await guard.Store().CreateAsync(Profile(), credential);
        Reliable &= result.Outcome == StoreOutcome.Success;
        AccountDatabaseGuard.Require(Reliable);
        s = guard.Open();
        try
        {
            var rows = s.Read("SELECT id FROM benutzer WHERE BINARY benutzername=BINARY @name AND BINARY vollstaendiger_name=BINARY @marker",
                ("@name", DbType.String, Name), ("@marker", DbType.String, FullName));
            AccountDatabaseGuard.Require(rows.Count == 1);
            Id = checked((int)AccountDatabaseGuard.Number(rows[0], "id"));
            AccountDatabaseGuard.Require(Owns(Run, Token, Id, Name, FullName, originals));
            Created = true;
        }
        finally { AccountDatabaseGuard.Require(!s.Close()); }
        var snapshot = await ReadAsync(guard, originals);
        AccountDatabaseGuard.Require(snapshot.CredentialRevision == 1 && snapshot.AccountRevision == 1 &&
            snapshot.Format == PasswordFormat.Pbkdf2Sha256V1);
    }
    internal void UseUnicodeFullName() => FullName = Marker + " Кириллица Äß Ω";
    internal void AssertOwned(AccountStoreSession s, IReadOnlySet<int> originals, bool locked = false)
    {
        AccountDatabaseGuard.Require(Created && Owns(Run, Token, Id, Name, FullName, originals));
        var rows = s.Read("SELECT id,benutzername,vollstaendiger_name FROM benutzer WHERE id=@id" + (locked ? " FOR UPDATE" : ""),
            ("@id", DbType.Int32, Id));
        AccountDatabaseGuard.Require(rows.Count == 1 &&
            AccountDatabaseGuard.Text(rows[0], "benutzername") == Name &&
            AccountDatabaseGuard.Text(rows[0], "vollstaendiger_name") == FullName);
    }
    internal async Task<AuthenticationSnapshot> ReadAsync(AccountDatabaseGuard guard, IReadOnlySet<int> originals)
    {
        var s = guard.Open();
        try { AssertOwned(s, originals); }
        finally { AccountDatabaseGuard.Require(!s.Close()); }
        // Only after exact ID+GUID marker ownership has been established.
        var snapshot = await guard.Store().FindAsync(Name, CancellationToken.None);
        AccountDatabaseGuard.Require(snapshot?.UserId == Id);
        return snapshot!;
    }
    internal void VerifyDirect(AccountStoreSession s, long cr, long ar, string? credential = null)
    {
        var rows = s.Read("SELECT credential_revision,account_revision,passwort FROM benutzer WHERE id=@id",
            ("@id", DbType.Int32, Id));
        AccountDatabaseGuard.Require(rows.Count == 1 &&
            AccountDatabaseGuard.Number(rows[0], "credential_revision") == cr &&
            AccountDatabaseGuard.Number(rows[0], "account_revision") == ar &&
            (credential == null || AccountDatabaseGuard.Text(rows[0], "passwort") == credential));
    }
    internal static bool MayClean(bool created, bool allFinished, bool reliable, bool verified, bool owned) =>
        created && allFinished && reliable && verified && owned;
    internal void Cleanup(AccountDatabaseGuard guard, IReadOnlySet<int> originals)
    {
        AccountDatabaseGuard.Require(MayClean(Created, ParticipantsFinished, Reliable, Verified,
            Owns(Run, Token, Id, Name, FullName, originals)) && !Cleaned);
        var s = guard.Open();
        var commitAttempted = false;
        var complete = false;
        try
        {
            s.Begin(); AssertOwned(s, originals, locked: true);
            AccountDatabaseGuard.Require(s.Execute(
                "DELETE FROM benutzer WHERE id=@id AND BINARY benutzername=BINARY @name AND BINARY vollstaendiger_name=BINARY @marker",
                ("@id", DbType.Int32, Id), ("@name", DbType.String, Name), ("@marker", DbType.String, FullName)) == 1);
            commitAttempted = true; s.Commit(); complete = true; Cleaned = true;
        }
        catch
        {
            Reliable = false;
            // Even an acknowledged rollback after a failed COMMIT is not proof of noncommit.
            if (!commitAttempted && s.Transaction != null)
            { try { s.Rollback(); } catch { s.Unusable = true; } }
            if (commitAttempted) s.Unusable = true;
            throw;
        }
        finally
        {
            var bad = !s.DisposeTransaction(); bad |= s.Close();
            if (bad) { Reliable = false; throw new InvalidOperationException(complete ?
                "Cleanup commit acknowledged; resource cleanup failed. Do not retry." : "Cleanup state unverified."); }
        }
    }
}