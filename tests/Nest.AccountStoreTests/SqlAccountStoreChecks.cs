using System.Data;
using Nest.Data.Authentication;
using Nest.Security.Authentication;
using static AccountStoreTests.Program;

namespace AccountStoreTests;
internal static class SqlAccountStoreChecks
{
    private static void Parameters(Call call, params string[] names) => Check(call.Parameters.Keys.Order().SequenceEqual(names.Order()), "exact parameter names");
    private static void Ordered(FakeDb db)
    {
        var begin = db.Trace.IndexOf("begin");
        Check(begin > db.Trace.IndexOf("schema"), "preflight schema before transaction");
        Check(db.Trace.IndexOf("metadata-pin") > begin && db.Trace.LastIndexOf("schema") > db.Trace.IndexOf("metadata-pin"), "recheck after transaction metadata pin");
        Check(db.Isolation == IsolationLevel.ReadCommitted, "explicit ReadCommitted");
        var lastSchema = db.Calls.FindLastIndex(c => c.Sql.Contains("information_schema", StringComparison.Ordinal));
        var firstMutation = db.Calls.FindIndex(c => c.Sql.StartsWith("UPDATE ", StringComparison.Ordinal) || c.Sql.StartsWith("INSERT ", StringComparison.Ordinal));
        Check(firstMutation < 0 || firstMutation > lastSchema, "no mutation before protected schema recheck");
        Check(db.Calls.Where(c => c.Sql.Contains("information_schema", StringComparison.Ordinal)).Count(c => c.InTransaction) == 4, "all four protected metadata queries enlisted");
        Check(db.Calls.Where(c => c.Sql.StartsWith("SELECT id", StringComparison.Ordinal) || c.Sql.StartsWith("UPDATE ", StringComparison.Ordinal) || c.Sql.StartsWith("INSERT ", StringComparison.Ordinal)).All(c => c.InTransaction), "account operations enlisted");
        Closed(db);
    }
    internal static async Task Run()
    {
        await Scenario("lookup uses exact username parameter and detached snapshot", async () =>
        {
            var db = new FakeDb(); var snapshot = await db.Store().FindAsync("Name ' OR 1=1 -- ", default);
            Check(snapshot is { UserId: 7, CredentialRevision: 4, AccountRevision: 9, Role: StaffRole.Admin });
            var call = db.Calls.Single(c => c.Sql.StartsWith("SELECT id", StringComparison.Ordinal));
            Parameters(call, "@name"); Check((string)call.Parameters["@name"]! == "Name ' OR 1=1 -- ");
            Check(!call.Sql.Contains("BINARY", StringComparison.Ordinal) && !call.Sql.Contains("FOR UPDATE", StringComparison.Ordinal));
            Check(!db.Trace.Contains("begin") && db.Writes == 0); Closed(db);
        });
        await Scenario("missing lookup", async () => { var db = new FakeDb { Row = null }; Check(await db.Store().FindAsync("synthetic", default) == null); Closed(db); });
        await Scenario("create exact explicit columns and initial revisions", async () =>
        {
            var db = new FakeDb(); Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.Success);
            var c = db.AccountCall;
            Parameters(c, "@name", "@fullName", "@role", "@status", "@credential", "@format", "@initialCredential", "@initialAccount");
            Check((long)c.Parameters["@initialCredential"]! == 1 && (long)c.Parameters["@initialAccount"]! == 1);
            Check((byte)c.Parameters["@format"]! == 1 && (string)c.Parameters["@credential"]! == Credential.Value);
            Check(c.Types["@initialCredential"] == DbType.Int64 && c.Types["@format"] == DbType.Byte);
            Check(c.Sql.Contains("(benutzername, vollstaendiger_name, rolle, status, passwort, password_format, credential_revision, account_revision, hinzugefuegt_am, geloescht_am)", StringComparison.Ordinal));
            Check(!c.Sql.Contains(Credential.Value, StringComparison.Ordinal)); Ordered(db);
        });
        await Scenario("finalize without rehash still commits", async () =>
        {
            var db = new FakeDb(); var result = await db.Store().FinalizeAsync(AuthStamp, null);
            Check(result.Outcome == StoreOutcome.Success && result.User?.Id == 7 && result.User.AccountRevision == 9);
            Check(db.Writes == 0 && db.Trace.Contains("commit"));
            var c = db.Calls.Single(c => c.Sql.Contains("FOR UPDATE", StringComparison.Ordinal)); Parameters(c, "@id"); Ordered(db);
        });
        await Scenario("rehash increments credential only", async () =>
        {
            var db = new FakeDb(); var result = await db.Store().FinalizeAsync(AuthStamp, Credential);
            Check(result.Outcome == StoreOutcome.Success && result.User != null);
            var c = db.AccountCall;
            Parameters(c, "@id", "@expectedCredential", "@expectedAccount", "@credential", "@format", "@nextCredential");
            Check((long)c.Parameters["@nextCredential"]! == 5 && !c.Parameters.ContainsKey("@nextAccount"));
            Check(!c.Sql.Split(" WHERE ")[0].Contains("account_revision=", StringComparison.Ordinal)); Check(db.Writes == 1); Ordered(db);
        });
        foreach (var kind in new[] { "profile", "password", "combined", "delete" })
        await Scenario("edit contract " + kind, async () =>
        {
            var db = new FakeDb(); var store = db.Store();
            var result = kind switch
            {
                "profile" => await store.UpdateProfileAsync(Stamp, Profile), "password" => await store.ChangeCredentialAsync(Stamp, Credential),
                "combined" => await store.UpdateProfileAndCredentialAsync(Stamp, Profile, Credential), _ => await store.SoftDeleteAsync(Stamp)
            };
            Check(result.Outcome == StoreOutcome.Success && db.Writes == 1);
            var c = db.AccountCall;
            var names = new List<string> { "@id", "@expectedCredential", "@expectedAccount", "@nextAccount" };
            if (kind is "profile" or "combined") names.AddRange(new[] { "@name", "@fullName", "@role", "@status" });
            if (kind is "password" or "combined") names.AddRange(new[] { "@credential", "@format", "@nextCredential" });
            Parameters(c, names.ToArray());
            Check((long)c.Parameters["@nextAccount"]! == 10 && (long)c.Parameters["@expectedCredential"]! == 4 && (long)c.Parameters["@expectedAccount"]! == 9);
            if (kind is "password" or "combined") Check((long)c.Parameters["@nextCredential"]! == 5 && (string)c.Parameters["@credential"]! == Credential.Value);
            else Check(!c.Sql.Split(" WHERE ")[0].Contains("passwort", StringComparison.Ordinal));
            Check(c.Sql.Contains("WHERE id=@id AND credential_revision=@expectedCredential AND account_revision=@expectedAccount AND geloescht_am IS NULL", StringComparison.Ordinal));
            Check(c.Sql.Contains("geloescht_am=CURRENT_TIMESTAMP", StringComparison.Ordinal) == (kind == "delete")); Ordered(db);
        });
        foreach (var mode in new[] { "missing", "credential-stale", "account-stale", "deleted" })
        foreach (var operation in new[] { "profile", "password", "combined", "delete", "finalize", "rehash" })
        await Scenario(mode + " rejected for " + operation, async () =>
        {
            var db = new FakeDb();
            if (mode == "missing") db.Row = null;
            else if (mode == "deleted") db.Row!["geloescht_am"] = new DateTime(2030, 1, 1);
            else db.Row![mode == "credential-stale" ? "credential_revision" : "account_revision"] = 10L;
            var store = db.Store(); StoreOutcome outcome;
            if (operation is "finalize" or "rehash") { var result = await store.FinalizeAsync(AuthStamp, operation == "rehash" ? Credential : null); outcome = result.Outcome; Check(result.User == null); }
            else outcome = (operation switch { "profile" => await store.UpdateProfileAsync(Stamp, Profile), "password" => await store.ChangeCredentialAsync(Stamp, Credential), "combined" => await store.UpdateProfileAndCredentialAsync(Stamp, Profile, Credential), _ => await store.SoftDeleteAsync(Stamp) }).Outcome;
            Check(outcome == StoreOutcome.ConcurrencyConflict && db.Writes == 0 && !db.Trace.Contains("commit")); Closed(db);
        });
        foreach (var state in new[] { "inactive", "legacy", "disabled" })
        await Scenario("finalization refuses " + state, async () =>
        {
            var db = new FakeDb(); if (state == "inactive") db.Row!["status"] = "Inaktiv"; else db.Row!["password_format"] = state == "legacy" ? (byte)0 : (byte)2;
            var result = await db.Store().FinalizeAsync(AuthStamp, Credential);
            Check(result.Outcome == StoreOutcome.Rejected && result.User == null && db.Writes == 0); Closed(db);
        });
        foreach (var column in new[] { "id", "credential_revision", "account_revision", "password_format", "rolle", "status", "benutzername", "passwort" })
        await Scenario("malformed row " + column, async () =>
        {
            var db = new FakeDb(); db.Row![column] = column switch { "id" => -1, "credential_revision" or "account_revision" => -1L, "password_format" => (byte)3, "rolle" or "status" => "Unknown", _ => null };
            var result = await db.Store().UpdateProfileAsync(Stamp, Profile);
            Check(result.Outcome == StoreOutcome.TechnicalFailure && db.Writes == 0); Closed(db);
        });
        foreach (var bound in new[] { 0L, long.MaxValue })
        await Scenario("nonincrementing finalization boundary " + (bound == 0 ? "zero" : "maximum"), async () =>
        {
            var db = new FakeDb(); db.Row!["credential_revision"] = bound; db.Row["account_revision"] = bound;
            Check((await db.Store().FinalizeAsync(new(7, bound, bound), null)).Outcome == StoreOutcome.Success && db.Writes == 0); Closed(db);
        });
        foreach (var kind in new[] { "profile", "password", "combined", "delete", "rehash" })
        await Scenario("exhaustion rejects before writing " + kind, async () =>
        {
            var db = new FakeDb(); db.Row!["credential_revision"] = long.MaxValue; db.Row["account_revision"] = long.MaxValue;
            var stamp = new AccountStamp(7, long.MaxValue, long.MaxValue); var store = db.Store();
            var outcome = kind switch { "profile" => (await store.UpdateProfileAsync(stamp, Profile)).Outcome, "password" => (await store.ChangeCredentialAsync(stamp, Credential)).Outcome, "combined" => (await store.UpdateProfileAndCredentialAsync(stamp, Profile, Credential)).Outcome, "delete" => (await store.SoftDeleteAsync(stamp)).Outcome, _ => (await store.FinalizeAsync(new(7, long.MaxValue, long.MaxValue), Credential)).Outcome };
            Check(outcome == StoreOutcome.Rejected && db.Writes == 0); Closed(db);
        });
        await Scenario("profile can keep max credential revision", async () => { var db = new FakeDb(); db.Row!["credential_revision"] = long.MaxValue; Check((await db.Store().UpdateProfileAsync(new(7, long.MaxValue, 9), Profile)).Outcome == StoreOutcome.Success); Closed(db); });
        await Scenario("rehash can keep max account revision", async () => { var db = new FakeDb(); db.Row!["account_revision"] = long.MaxValue; Check((await db.Store().FinalizeAsync(new(7, 4, long.MaxValue), Credential)).Outcome == StoreOutcome.Success); Closed(db); });
        foreach (var value in new[] { " Name ", "", new string('x', 50), string.Concat(Enumerable.Repeat("\U0001F600", 50)) })
        await Scenario("profile preserves valid literal input", async () => { var db = new FakeDb(); Check((await db.Store().CreateAsync(Profile with { UserName = value }, Credential)).Outcome == StoreOutcome.Success); Check((string)db.AccountCall.Parameters["@name"]! == value); Closed(db); });
        foreach (var invalid in new[] { new string('x', 51), "\ud800", string.Concat(Enumerable.Repeat("\U0001F600", 51)) })
        await Scenario("reject unrepresentable name without opening connection", async () => { var db = new FakeDb(); Check((await db.Store().CreateAsync(Profile with { UserName = invalid }, Credential)).Outcome == StoreOutcome.Rejected); Check(db.Trace.Count == 0); });
        foreach (var kind in new[] { "negative-credential", "negative-account", "zero-id", "unknown-role", "unknown-status", "long-full-name", "null-credential", "null-profile" })
        await Scenario("reject invalid caller request " + kind, async () =>
        {
            var db = new FakeDb(); var store = db.Store();
            var outcome = kind switch
            {
                "negative-credential" => (await store.UpdateProfileAsync(new(7, -1, 9), Profile)).Outcome,
                "negative-account" => (await store.UpdateProfileAsync(new(7, 4, -1), Profile)).Outcome,
                "zero-id" => (await store.SoftDeleteAsync(new(0, 4, 9))).Outcome,
                "unknown-role" => (await store.CreateAsync(Profile with { Role = (StaffRole)99 }, Credential)).Outcome,
                "unknown-status" => (await store.CreateAsync(Profile with { Status = (StaffStatus)99 }, Credential)).Outcome,
                "long-full-name" => (await store.CreateAsync(Profile with { FullName = new string('x', 101) }, Credential)).Outcome,
                "null-credential" => (await store.ChangeCredentialAsync(Stamp, null!)).Outcome,
                _ => (await store.UpdateProfileAsync(Stamp, null!)).Outcome
            };
            Check(outcome == StoreOutcome.Rejected && db.Trace.Count == 0);
        });
        foreach (var name in new[] { "credential_revision", "account_revision", "password_format" })
        await Scenario("NULL numeric state rejected " + name, async () =>
        {
            var db = new FakeDb(); db.Row![name] = null;
            Check((await db.Store().UpdateProfileAsync(Stamp, Profile)).Outcome == StoreOutcome.TechnicalFailure && db.Writes == 0); Closed(db);
        });
        await Scenario("zero baseline advances exactly once", async () =>
        {
            var db = new FakeDb(); db.Row!["credential_revision"] = 0L; db.Row["account_revision"] = 0L;
            Check((await db.Store().UpdateProfileAndCredentialAsync(new(7, 0, 0), Profile, Credential)).Outcome == StoreOutcome.Success);
            Check((long)db.AccountCall.Parameters["@nextCredential"]! == 1 && (long)db.AccountCall.Parameters["@nextAccount"]! == 1); Closed(db);
        });
        await Scenario("last available revisions reach maximum without wrapping", async () =>
        {
            var db = new FakeDb(); db.Row!["credential_revision"] = long.MaxValue - 1; db.Row["account_revision"] = long.MaxValue - 1;
            Check((await db.Store().UpdateProfileAndCredentialAsync(new(7, long.MaxValue - 1, long.MaxValue - 1), Profile, Credential)).Outcome == StoreOutcome.Success);
            Check((long)db.AccountCall.Parameters["@nextCredential"]! == long.MaxValue && (long)db.AccountCall.Parameters["@nextAccount"]! == long.MaxValue); Closed(db);
        });
        foreach (var input in new[] { "Synthetic" + new string(' ', 50), new string('x', 1024), string.Concat(Enumerable.Repeat("\U0001F600", 1024)) })
        await Scenario("bounded lookup preserves long literal and padding", async () =>
        {
            var db = new FakeDb(); await db.Store().FindAsync(input, default);
            var call = db.Calls.Single(c => c.Sql.StartsWith("SELECT id,", StringComparison.Ordinal));
            Check((string)call.Parameters["@name"]! == input); Closed(db);
        });
        await Scenario("oversized lookup is an error rather than missing account", async () =>
        {
            var db = new FakeDb(); var ex = await Error(async () => { await db.Store().FindAsync(new string('x', 1025), default); });
            Check(ex is InvalidOperationException && db.Trace.Count == 0);
        });
        await Scenario("schema drift between preflight and MDL is rejected", async () =>
        {
            var db = new FakeDb(); db.OnBegin = () => db.Engine = "MyISAM";
            Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.TechnicalFailure && db.Writes == 0);
            Check(db.Trace.Contains("metadata-pin") && db.Trace.Contains("rollback")); Closed(db);
        });
        foreach (var change in InvalidSchemas())
        await Scenario("schema refuses " + change.Name, async () => { var db = new FakeDb(); change.Apply(db); Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.TechnicalFailure); Check(!db.Trace.Contains("begin") && db.Writes == 0 && db.ConnectionReleased); Closed(db); });
        await Scenario("parenthesized canonical constraints accepted", async () => { var db = new FakeDb(); foreach (var row in db.Checks) row["CHECK_CLAUSE"] = " ( ( " + row["CHECK_CLAUSE"] + " ) ) "; Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.Success); Closed(db); });
        await Scenario("restores original modes and both lock timeouts", async () => { var db = new FakeDb { Mode = "NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,STRICT_ALL_TABLES" }; Check((await db.Store().CreateAsync(Profile, Credential)).Outcome == StoreOutcome.Success); Check(db.Mode == "NO_ENGINE_SUBSTITUTION,ANSI_QUOTES,STRICT_ALL_TABLES" && db.RowWait == 50 && db.MetadataWait == 86400 && db.ConnectionReleased); Closed(db); });

    }
    internal static async Task DbConfigurationChecks()
    {
        foreach (var kind in new[] { "configuration", "valid-normal", "valid-account", "tampered-normal", "tampered-account", "tampered-timeout", "foreign", "clone", "invalid-target", "invalid-mode", "restricted-options", "changed-process" })
        await Scenario("central DB contract: " + kind, async () =>
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("--db-contract"); start.ArgumentList.Add(kind);
            foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("NEST_ALLOW_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
            start.Environment["NEST_INTEGRATION_TEST"] = "1";
            start.Environment["NEST_INTEGRATION_CONNECTION_STRING"] = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder {
                Server = "localhost", Port = 3306, Database = "nest_integration_test", UserID = "nest_test_runner", Password = "synthetic-offline-only", UseAffectedRows = false }.ConnectionString;
            using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException();
            var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Check(process.ExitCode == 0 && (await output).Trim() == "DB_CONTRACT_PASS" && (await errors).Length == 0, "isolated factory case");
            }
            finally
            {
                // Only the child created here may be stopped; no existing/user process is touched.
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                await Task.WhenAll(output, errors);
            }
        });
    }
    internal static int DbChild(string[] args)
    {
        if (args.Length != 2 || args[0] != "--db-contract") throw new InvalidOperationException();
        var kind = args[1];
        if (kind == "restricted-options")
        {
            var valid = Environment.GetEnvironmentVariable("NEST_INTEGRATION_CONNECTION_STRING")!;
            foreach (var change in new (string Key, object Value)[] { ("Pooling", false), ("Default Command Timeout", 10u), ("Server", "127.0.0.1"), ("Port", 3307u), ("User ID", "root"), ("UseAffectedRows", true) })
            {
                var invalid = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(valid) { [change.Key] = change.Value };
                Environment.SetEnvironmentVariable("NEST_INTEGRATION_CONNECTION_STRING", invalid.ConnectionString);
                try { using var unexpected = Nest.Data.DB.GetAccountStoreConnection(); throw new Exception(); }
                catch (InvalidOperationException) { }
            }
            Console.WriteLine("DB_CONTRACT_PASS"); return 0;
        }
        if (kind is "invalid-target" or "invalid-mode")
        {
            if (kind == "invalid-mode") Environment.SetEnvironmentVariable("NEST_INTEGRATION_TEST", "0");
            else
            {
                var invalid = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEST_INTEGRATION_CONNECTION_STRING")) { Database = "hotel" };
                Environment.SetEnvironmentVariable("NEST_INTEGRATION_CONNECTION_STRING", invalid.ConnectionString);
            }
            try { using var unexpected = Nest.Data.DB.GetAccountStoreConnection(); throw new Exception(); }
            catch (InvalidOperationException) { Console.WriteLine("DB_CONTRACT_PASS"); return 0; }
        }
        using var ordinary = Nest.Data.DB.GetConnection();
        var ordinarySettings = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(ordinary.ConnectionString);
        using var account = Nest.Data.DB.GetAccountStoreConnection();
        var accountSettings = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(account.ConnectionString);
        if (ordinarySettings.DefaultCommandTimeout != 30 || accountSettings.DefaultCommandTimeout != 10 || accountSettings.Pooling || accountSettings.UseAffectedRows ||
            accountSettings.Server != "localhost" || accountSettings.Port != 3306 || accountSettings.Database != "nest_integration_test" || accountSettings.UserID != "nest_test_runner") throw new InvalidOperationException();
        using var accountCommand = account.CreateCommand(); using var ordinaryCommand = ordinary.CreateCommand();
        if (accountCommand.CommandTimeout != 10 || ordinaryCommand.CommandTimeout != 30) throw new InvalidOperationException();
        var baseline = Nest.Data.IntegrationConnectionSettings.FromEnvironment(true)!;
        if (ordinary.ConnectionString != baseline.ConnectionString) throw new InvalidOperationException();
        using var ordinaryAgain = Nest.Data.DB.GetConnection();
        if (ordinaryAgain.ConnectionString != ordinary.ConnectionString) throw new InvalidOperationException();
        if (kind == "configuration") { Console.WriteLine("DB_CONTRACT_PASS"); return 0; }
        if (kind == "changed-process")
        {
            Environment.SetEnvironmentVariable("NEST_INTEGRATION_TEST", "0");
            try { using var unexpected = Nest.Data.DB.GetAccountStoreConnection(); throw new Exception(); }
            catch (InvalidOperationException) { }
            var reached = false;
            account.StateChange += (_, e) => { if (e.CurrentState == ConnectionState.Connecting) { reached = true; throw new InvalidOperationException("Offline transport barrier."); } };
            try { Nest.Data.DB.OpenConnection(account); throw new Exception(); } catch (InvalidOperationException) { }
            if (reached) throw new Exception();
            Console.WriteLine("DB_CONTRACT_PASS"); return 0;
        }
        using var foreign = new MySql.Data.MySqlClient.MySqlConnection(account.ConnectionString);
        using var clone = account.CloneWith(account.ConnectionString);
        var candidate = kind switch { "valid-normal" or "tampered-normal" => ordinary, "foreign" => foreign, "clone" => clone, _ => account };
        if (kind.StartsWith("tampered", StringComparison.Ordinal))
        {
            var changed = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(candidate.ConnectionString);
            if (kind == "tampered-timeout") changed.DefaultCommandTimeout = 11; else changed.Pooling = true;
            candidate.ConnectionString = changed.ConnectionString;
        }
        var reachedTransportBoundary = false;
        // MySql.Data 9.7.0 raises Connecting before DNS, pool lookup or Driver.Create.
        // Even a broken admission guard cannot make these tests send a database packet.
        candidate.StateChange += (_, e) => { if (e.CurrentState == ConnectionState.Connecting) { reachedTransportBoundary = true; throw new InvalidOperationException("Offline transport barrier."); } };
        try { Nest.Data.DB.OpenConnection(candidate); throw new Exception(); }
        catch (InvalidOperationException)
        {
            if (reachedTransportBoundary != kind.StartsWith("valid-", StringComparison.Ordinal)) throw new Exception();
        }
        Console.WriteLine("DB_CONTRACT_PASS"); return 0;
    }
    private static IEnumerable<(string Name, Action<FakeDb> Apply)> InvalidSchemas()
    {
        yield return ("engine", db => db.Engine = "MyISAM"); yield return ("collation", db => db.Collation = "utf8mb4_bin");
        foreach (var name in new[] { "id", "benutzername", "passwort", "vollstaendiger_name", "rolle", "status", "hinzugefuegt_am", "geloescht_am", "password_format", "credential_revision", "account_revision" })
        {
            yield return (name + " missing", db => db.Columns.RemoveAll(r => (string)r["COLUMN_NAME"]! == name));
            yield return (name + " type", db => db.Columns.Single(r => (string)r["COLUMN_NAME"]! == name)["COLUMN_TYPE"] = "bigint(20) unsigned");
            yield return (name + " generated", db => db.Columns.Single(r => (string)r["COLUMN_NAME"]! == name)["GENERATION_EXPRESSION"] = "1");
        }
        foreach (var name in new[] { "password_format", "credential_revision", "account_revision" })
        {
            yield return (name + " nullable", db => db.Columns.Single(r => (string)r["COLUMN_NAME"]! == name)["IS_NULLABLE"] = "YES");
            yield return (name + " default", db => db.Columns.Single(r => (string)r["COLUMN_NAME"]! == name)["COLUMN_DEFAULT"] = "0");
        }
        yield return ("primary missing", db => db.Indexes.RemoveAt(0));
        yield return ("unique lost", db => db.Indexes[1]["NON_UNIQUE"] = 1);
        yield return ("prefix unique", db => db.Indexes[1]["SUB_PART"] = 10L);
        yield return ("index wrong column", db => db.Indexes[1]["COLUMN_NAME"] = "vollstaendiger_name");
        yield return ("check missing", db => db.Checks.RemoveAt(0));
        yield return ("check renamed", db => db.Checks[0]["CONSTRAINT_NAME"] = "wrong");
        yield return ("format weakened", db => db.Checks[0]["CHECK_CLAUSE"] = "password_format IN (0,1,2,3)");
        yield return ("check bypass", db => db.Checks[0]["CHECK_CLAUSE"] = "password_format IN (0,1,2) OR 1=1");
        yield return ("credential upper bound", db => db.Checks[1]["CHECK_CLAUSE"] = "credential_revision between 0 and 9223372036854775806");
        yield return ("account negative allowed", db => db.Checks[2]["CHECK_CLAUSE"] = "account_revision between -1 and 9223372036854775807");
        yield return ("metadata inaccessible", db => db.ThrowSqlContaining = "information_schema.CHECK_CONSTRAINTS");
    }
}
