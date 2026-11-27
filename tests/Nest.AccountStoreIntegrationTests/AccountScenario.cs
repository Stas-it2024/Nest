using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;
using Nest.Data.Authentication;
using Nest.Security;
using Nest.Security.Authentication;

internal static class AccountScenario
{
    internal static readonly string[] Names =
    {
        "lifecycle-seven-methods", "collation-unicode-duplicate-affected-rows",
        "strict-mode-and-check-enforcement", "signed-revision-boundaries", "real-service-rehash",
        "concurrent-profile-edits", "profile-versus-password", "two-simultaneous-rehashes",
        "login-versus-deactivation", "login-versus-password-change", "login-versus-soft-delete"
    };
    internal static async Task<int> RunAsync(AccountDatabaseGuard guard)
    {
        var run = Guid.NewGuid();
        var fixtures = new List<AccountFixture>();
        HashSet<int>? original = null;
        Console.WriteLine($"RUN {run:N}");
        try
        {
            original = guard.Preflight(); // Zero account mutations before complete preflight.
            foreach (var name in Names)
            {
                var f = new AccountFixture(run); fixtures.Add(f);
                Console.WriteLine($"START {name}; token={f.Token:N}");
                await f.CreateAsync(guard, original);
                switch (name)
                {
                    case "lifecycle-seven-methods": await Lifecycle(guard, f, original); break;
                    case "collation-unicode-duplicate-affected-rows": await Collation(guard, f, original); break;
                    case "strict-mode-and-check-enforcement": Checks(guard, f, original); break;
                    case "signed-revision-boundaries": await Revisions(guard, f, original); break;
                    case "real-service-rehash": await Rehash(guard, f, original); break;
                    case "concurrent-profile-edits": await Concurrent(guard, f, original, "profile", "profile"); break;
                    case "profile-versus-password": await Concurrent(guard, f, original, "profile", "password"); break;
                    case "two-simultaneous-rehashes": await Concurrent(guard, f, original, "rehash", "rehash"); break;
                    default: await LoginRace(guard, f, original, name); break;
                }
                f.Verified = true;
                await guard.AssertConnectionsReleasedAsync();
                f.Cleanup(guard, original);
                AssertOriginalIds(guard, original);
                Console.WriteLine($"PASS {name}; fixture id={f.Id}; cleanup acknowledged");
            }
            await guard.AssertConnectionsReleasedAsync();
            Console.WriteLine("PASS: all scenarios; own fixtures removed; original ID set/count retained.");
            Console.WriteLine("Original credentials/profile fields were never selected or compared.");
            return 0;
        }
        catch (Exception error)
        {
            AccountDatabaseGuard.ReportFailure(error);
            Console.WriteLine("NOT VERIFIED: STOP. No further scenarios, retry, or automatic cleanup.");
            foreach (var f in fixtures.Where(x => !x.Cleaned))
                Console.WriteLine($"RETAIN run={f.Run:N} token={f.Token:N} id={f.Id}; finished={f.ParticipantsFinished}; reliable={f.Reliable}");
            return 2;
        }
    }
    private static void Expect(StoreOutcome result, StoreOutcome expected = StoreOutcome.Success)
    { AccountDatabaseGuard.Require(result == expected); }
    private static async Task<AuthenticationSnapshot> RevisionsAre(AccountDatabaseGuard g, AccountFixture f,
        IReadOnlySet<int> originals, long cr, long ar)
    {
        var s = await f.ReadAsync(g, originals);
        AccountDatabaseGuard.Require(s.CredentialRevision == cr && s.AccountRevision == ar); return s;
    }
    private static async Task Lifecycle(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original)
    {
        var store = g.Store(); var row = await RevisionsAre(g, f, original, 1, 1);
        var finalized = await store.FinalizeAsync(row.Stamp, null);
        Expect(finalized.Outcome); AccountDatabaseGuard.Require(finalized.User?.Id == f.Id);
        Expect((await store.UpdateProfileAsync(f.Stamp(row), f.Profile() with { Role = StaffRole.Admin })).Outcome);
        row = await RevisionsAre(g, f, original, 1, 2);
        AccountDatabaseGuard.Require(row.Role == StaffRole.Admin);
        var replacement = await PreparedCredential.CreateAsync(AccountFixture.ReplacementPassword);
        Expect((await store.ChangeCredentialAsync(f.Stamp(row), replacement)).Outcome);
        row = await RevisionsAre(g, f, original, 2, 3);
        AccountDatabaseGuard.Require(row.Credential == replacement.Value && row.Role == StaffRole.Admin);
        var credential = await PreparedCredential.CreateAsync(AccountFixture.Password);
        Expect((await store.UpdateProfileAndCredentialAsync(f.Stamp(row), f.Profile(), credential)).Outcome);
        row = await RevisionsAre(g, f, original, 3, 4);
        AccountDatabaseGuard.Require(row.Credential == credential.Value && row.Role == StaffRole.Receptionist);
        var login = await new AuthenticationService(store).AuthenticateAsync(new(f.Name, AccountFixture.Password));
        AccountDatabaseGuard.Require(login.IsAuthenticated && login.FinalizationOutcome == StoreOutcome.Success);
        var wrong = await new AuthenticationService(store).AuthenticateAsync(new(f.Name, AccountFixture.Password.ToUpperInvariant()));
        AccountDatabaseGuard.Require(!wrong.IsAuthenticated && wrong.Outcome == AuthenticationOutcome.Rejected);
        Expect((await store.SoftDeleteAsync(f.Stamp(row))).Outcome);
        row = await RevisionsAre(g, f, original, 3, 5);
        AccountDatabaseGuard.Require(row.DeletedAt.HasValue);
        Expect((await store.FinalizeAsync(row.Stamp, null)).Outcome, StoreOutcome.ConcurrencyConflict);
    }
    private static async Task Collation(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original)
    {
        var store = g.Store();
        foreach (var alias in new[] { f.Name.ToUpperInvariant(), f.Name + "   ", f.Name[..^1] + "a" })
        {
            var found = await store.FindAsync(alias, CancellationToken.None);
            AccountDatabaseGuard.Require(found?.UserId == f.Id);
            Expect((await store.CreateAsync(new(alias, f.Marker, StaffRole.Admin, StaffStatus.Active),
                await PreparedCredential.CreateAsync(AccountFixture.Password))).Outcome, StoreOutcome.Rejected);
        }
        var row = await f.ReadAsync(g, original); f.UseUnicodeFullName();
        Expect((await store.UpdateProfileAsync(f.Stamp(row), f.Profile() with { Role = StaffRole.Admin })).Outcome);
        row = await RevisionsAre(g, f, original, 1, 2);
        AccountDatabaseGuard.Require(row.Role == StaffRole.Admin);
        // An unchanged value must still count as a matched row with UseAffectedRows=false.
        Transaction(g, f, original, s => AccountDatabaseGuard.Require(s.Execute(
            "UPDATE benutzer SET vollstaendiger_name=@marker WHERE id=@id",
            ("@marker", DbType.String, f.FullName), ("@id", DbType.Int32, f.Id)) == 1), commit: false);
        var read = g.Open();
        try { f.AssertOwned(read, original); f.VerifyDirect(read, 1, 2, row.Credential); }
        finally { AccountDatabaseGuard.Require(!read.Close()); }
    }
    private static void Transaction(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original,
        Action<AccountStoreSession> body, bool commit)
    {
        var s = g.Open(); var completed = false; var commitAttempted = false;
        try
        {
            s.Begin(); f.AssertOwned(s, original, locked: true); body(s);
            if (commit) { commitAttempted = true; s.Commit(); } else s.Rollback();
            completed = true;
        }
        catch
        {
            if (!commitAttempted && s.Transaction != null)
            { try { s.Rollback(); } catch { f.Reliable = false; s.Unusable = true; } }
            if (commitAttempted) { f.Reliable = false; s.Unusable = true; }
            throw;
        }
        finally
        {
            var bad = !s.DisposeTransaction(); bad |= s.Close();
            if (!completed || bad) f.Reliable = false;
            AccountDatabaseGuard.Require(completed && !bad);
        }
    }
    private static void Checks(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original)
    {
        // No DDL: attempt invalid values ONLY on the confirmed fixture and always ROLLBACK.
        var probes = new (string Column, object Value, DbType Type, int Error)[]
        {
            ("password_format", (byte)3, DbType.Byte, 4025),
            ("credential_revision", -1L, DbType.Int64, 4025),
            ("account_revision", -1L, DbType.Int64, 4025),
            ("password_format", 256, DbType.Int32, 1264),
            ("credential_revision", "9223372036854775808", DbType.String, 1264),
            ("account_revision", "9223372036854775808", DbType.String, 1264),
            ("vollstaendiger_name", new string('X',101), DbType.String, 1406)
        };
        foreach (var p in probes)
        {
            Transaction(g, f, original, s =>
            {
                var rejected = false;
                try
                {
                    // Column comes exclusively from the closed probe list above; all values parameterized.
                    s.Execute("UPDATE benutzer SET " + p.Column + "=@value WHERE id=@id",
                        ("@value", p.Type, p.Value), ("@id", DbType.Int32, f.Id));
                }
                catch (MySqlException error) when (error.Number == p.Error) { rejected = true; }
                AccountDatabaseGuard.Require(rejected);
            }, commit: false);
        }
        var read = g.Open();
        try { f.AssertOwned(read, original); f.VerifyDirect(read, 1, 1); }
        finally { AccountDatabaseGuard.Require(!read.Close()); }
    }
    private static async Task Revisions(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original)
    {
        Transaction(g, f, original, s => AccountDatabaseGuard.Require(s.Execute(
            "UPDATE benutzer SET credential_revision=@max,account_revision=@max WHERE id=@id",
            ("@max", DbType.Int64, long.MaxValue), ("@id", DbType.Int32, f.Id)) == 1), commit: true);
        var row = await RevisionsAre(g, f, original, long.MaxValue, long.MaxValue);
        var store = g.Store();
        Expect((await store.FinalizeAsync(row.Stamp, null)).Outcome);
        Expect((await store.UpdateProfileAsync(f.Stamp(row), f.Profile())).Outcome, StoreOutcome.Rejected);
        Expect((await store.ChangeCredentialAsync(f.Stamp(row),
            await PreparedCredential.CreateAsync(AccountFixture.ReplacementPassword))).Outcome, StoreOutcome.Rejected);
        await RevisionsAre(g, f, original, long.MaxValue, long.MaxValue);
    }
    private static string SeedRehash(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16), bytes = Encoding.UTF8.GetBytes(AccountFixture.Password);
        byte[]? hash = null; string encoded;
        try
        {
            hash = Rfc2898DeriveBytes.Pbkdf2(bytes, salt, 100000, HashAlgorithmName.SHA256, 32);
            encoded = "$nest$pbkdf2-sha256$v1$100000$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
        }
        finally { CryptographicOperations.ZeroMemory(salt); CryptographicOperations.ZeroMemory(bytes); if (hash != null) CryptographicOperations.ZeroMemory(hash); }
        AccountDatabaseGuard.Require(PasswordHasher.NeedsRehash(encoded));
        Transaction(g, f, original, s => AccountDatabaseGuard.Require(s.Execute(
            "UPDATE benutzer SET passwort=@credential WHERE id=@id",
            ("@credential", DbType.String, encoded), ("@id", DbType.Int32, f.Id)) == 1), commit: true);
        return encoded;
    }
    private static async Task Rehash(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original)
    {
        var encoded = SeedRehash(g, f, original);
        var login = await new AuthenticationService(g.Store()).AuthenticateAsync(new(f.Name, AccountFixture.Password));
        AccountDatabaseGuard.Require(login.IsAuthenticated && login.FinalizationOutcome == StoreOutcome.Success);
        var row = await RevisionsAre(g, f, original, 2, 1);
        AccountDatabaseGuard.Require(row.Credential != encoded && !PasswordHasher.NeedsRehash(row.Credential!));
    }
    private static async Task Concurrent(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original, string a, string b)
    {
        if (a == "rehash" && b == "rehash") SeedRehash(g, f, original);
        var workers = new[] { AccountWorker.Edit(g, f, original, a), AccountWorker.Edit(g, f, original, b) };
        f.ParticipantsFinished = false;
        AccountStoreSession? blocker = null, observer = null; var released = false;
        try
        {
            await Task.WhenAll(workers.Select(x => x.Ready)).WaitAsync(TimeSpan.FromSeconds(20));
            observer = g.Open(); blocker = g.Open(); blocker.Begin(); f.AssertOwned(blocker, original, locked: true);
            foreach (var worker in workers) worker.Go();
            // Both actual production FOR UPDATE commands must be observed while this connection
            // holds the very same row lock. Observation is overlap evidence, NOT FIFO evidence.
            var deadline = Stopwatch.StartNew(); var observed = new HashSet<long>();
            while (observed.Count < workers.Length)
            {
                foreach (var id in workers.SelectMany(x => x.Connections).Distinct())
                {
                    var rows = observer.Read("SELECT ID FROM information_schema.PROCESSLIST WHERE ID=@id AND USER='nest_test_runner' AND DB=DATABASE() AND LOCATE('FOR UPDATE',UPPER(COALESCE(INFO,'')))>0",
                        ("@id", DbType.Int64, id));
                    if (rows.Count == 1) observed.Add(id);
                }
                AccountDatabaseGuard.Require(deadline.Elapsed < TimeSpan.FromSeconds(2.5));
                if (observed.Count < workers.Length) await Task.Delay(15);
            }
            blocker.Rollback(); released = true;
            AccountDatabaseGuard.Require(blocker.DisposeTransaction() && !blocker.Close()); blocker = null;
            var results = await Task.WhenAll(workers.Select(x => x.Completion)).WaitAsync(TimeSpan.FromSeconds(20));
            AccountDatabaseGuard.Require(results.All(x => x.Reliable) &&
                results.Count(x => x.Outcome == StoreOutcome.Success) == 1 &&
                results.Count(x => x.Outcome == StoreOutcome.ConcurrencyConflict) == 1);
            var winner = Array.FindIndex(results, x => x.Outcome == StoreOutcome.Success);
            var operation = winner == 0 ? a : b;
            var cr = operation is "password" or "rehash" ? 2 : 1;
            var ar = operation == "rehash" ? 1 : 2;
            var row = await RevisionsAre(g, f, original, cr, ar);
            AccountDatabaseGuard.Require(row.Role == (operation == "profile" ? StaffRole.Admin : StaffRole.Receptionist));
            var expectedPassword = operation == "password" ? AccountFixture.ReplacementPassword : AccountFixture.Password;
            AccountDatabaseGuard.Require(PasswordHasher.VerifyPassword(expectedPassword, row.Credential) == PasswordVerificationResult.Success);
        }
        catch { f.Reliable = false; throw; }
        finally
        {
            if (blocker != null)
            {
                if (!released) { try { blocker.Rollback(); } catch { f.Reliable = false; blocker.Unusable = true; } }
                var bad = !blocker.DisposeTransaction(); bad |= blocker.Close(); if (bad) f.Reliable = false;
            }
            if (observer != null && observer.Close()) f.Reliable = false;
            f.ParticipantsFinished = await AccountWorker.FinishAsync(workers);
            AccountDatabaseGuard.Require(f.ParticipantsFinished && f.Reliable);
        }
    }
    private static async Task LoginRace(AccountDatabaseGuard g, AccountFixture f, IReadOnlySet<int> original, string name)
    {
        var worker = AccountWorker.Login(g, f); f.ParticipantsFinished = false;
        try
        {
            await worker.Ready.WaitAsync(TimeSpan.FromSeconds(20)); // Real password verified, finalization not yet admitted.
            var row = await f.ReadAsync(g, original); var store = g.Store();
            var update = name switch
            {
                "login-versus-deactivation" => await store.UpdateProfileAsync(f.Stamp(row), f.Profile(StaffStatus.Inactive)),
                "login-versus-password-change" => await store.ChangeCredentialAsync(f.Stamp(row),
                    await PreparedCredential.CreateAsync(AccountFixture.ReplacementPassword)),
                "login-versus-soft-delete" => await store.SoftDeleteAsync(f.Stamp(row)),
                _ => throw new InvalidOperationException()
            };
            Expect(update.Outcome); worker.Go();
            var result = await worker.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            AccountDatabaseGuard.Require(result.Reliable && !result.Authenticated && result.Outcome == StoreOutcome.ConcurrencyConflict);
            var current = await RevisionsAre(g, f, original, name == "login-versus-password-change" ? 2 : 1, 2);
            AccountDatabaseGuard.Require(name switch
            {
                "login-versus-deactivation" => current.Status == StaffStatus.Inactive,
                "login-versus-soft-delete" => current.DeletedAt.HasValue,
                _ => PasswordHasher.VerifyPassword(AccountFixture.ReplacementPassword, current.Credential) == PasswordVerificationResult.Success
            });
        }
        catch { f.Reliable = false; throw; }
        finally
        {
            f.ParticipantsFinished = await AccountWorker.FinishAsync(new[] { worker });
            AccountDatabaseGuard.Require(f.ParticipantsFinished && f.Reliable);
        }
    }
    private static void AssertOriginalIds(AccountDatabaseGuard g, IReadOnlySet<int> original)
    {
        var s = g.Open();
        try { AccountDatabaseGuard.Require(s.Read("SELECT id FROM benutzer")
            .Select(x => checked((int)AccountDatabaseGuard.Number(x, "id"))).ToHashSet().SetEquals(original)); }
        finally { AccountDatabaseGuard.Require(!s.Close()); }
    }
}