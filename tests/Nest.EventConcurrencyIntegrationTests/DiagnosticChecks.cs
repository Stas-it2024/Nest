using MySql.Data.MySqlClient;
using Nest.Data;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class DiagnosticChecks
{
    private static int checks;
    private const string DummyPassword = "non-secret-diagnostic-offline-placeholder";
    private const string SyntheticError = "synthetic-sensitive-detail-not-a-credential";
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("Diagnostic offline check failed: " + name); checks++; }
    private static Dictionary<string, string?> Environment() => new(StringComparer.OrdinalIgnoreCase)
    { [IntegrationConnectionSettings.ModeVariable] = "1" };
    private static MySqlConnectionStringBuilder Builder() => new()
    {
        Server = "localhost", Port = 3306, Database = "nest_integration_test", UserID = "nest_test_runner",
        Password = DummyPassword, UseAffectedRows = false
    };
    internal static void Run()
    {
        Check(EventDatabaseGuard.Offline, "real transports/credentials are disabled");
        ConfigurationChecks(); MetadataChecks(); ReadOnlyChecks();
        Check(EventDiagnostics.CredentialReads == 0 && EventDiagnostics.ConnectionAttempts == 0 &&
            EventDatabaseGuard.ConnectionsOpened == 0 && EventDatabaseGuard.CredentialsRead == 0, "zero real connection attempts and credential reads");
        Console.WriteLine($"PASS: {checks} diagnostic offline checks; fake read-only sessions only, no Credential Manager or MariaDB access.");
    }
    private static void ConfigurationChecks()
    {
        DiagnosticReport Evaluate(Dictionary<string, string?> environment, string[]? args = null, bool credentialMissing = false)
        {
            return EventDiagnostics.Evaluate(args ?? new[] { Program.Diagnose }, environment, () =>
            {
                credentials++;
                if (credentialMissing) throw new InvalidOperationException(SyntheticError);
                return DummyPassword;
            }, factory => { activations++; return EventDiagnostics.Inspect(factory); }, configuration =>
            {
                var parsed = IntegrationConnectionSettings.Parse(configuration);
                Check(parsed.UserName == "nest_test_runner", "only runner activation");
                return new FakeDatabase().Open;
            });
        }
        foreach (var flag in EventDiagnostics.WriteFlags)
        foreach (string? value in new string?[] { null, "", "0", "nest_integration_test" })
        {
            credentials = activations = 0;
            var environment = Environment(); environment[flag] = value;
            var result = Evaluate(environment);
            Check(result.ExitCode == 2 && credentials == 0 && activations == 0, "presence of write flag refuses before credentials");
        }
        foreach (string? mode in new string?[] { null, "", "0", "true" })
        {
            credentials = activations = 0; var environment = Environment(); environment[IntegrationConnectionSettings.ModeVariable] = mode;
            Check(Evaluate(environment).ExitCode == 2 && credentials == 0 && activations == 0, "explicit mode required");
        }
        foreach (var args in new[] { Array.Empty<string>(), new[] { Program.Execute }, new[] { Program.Diagnose, Program.Execute },
            new[] { Program.Execute, Program.Diagnose }, new[] { Program.Diagnose, "--worker" }, new[] { Program.Diagnose, Program.Diagnose } })
        {
            credentials = activations = 0;
            Check(Evaluate(Environment(), args).ExitCode == 2 && credentials == 0 && activations == 0, "mixed/other modes cannot activate diagnostics");
        }
        Check(Program.Diagnostics(new[] { Program.Diagnose }) && !Program.Controller(new[] { Program.Diagnose }) &&
            !Program.Worker(new[] { Program.Diagnose }), "separate command branch");
        foreach (var change in new (string Key, object Value)[]
        {
            ("Database", "hotel"), ("Database", "other"), ("User ID", "root"), ("User ID", "nest_test_app"),
            ("Server", "127.0.0.1"), ("Server", "remote.invalid"), ("Port", 3307u), ("UseAffectedRows", true), ("Pooling", true), ("Password", "")
        })
        {
            var builder = Builder(); builder[change.Key] = change.Value;
            var environment = Environment(); environment[IntegrationConnectionSettings.ConnectionVariable] = builder.ConnectionString;
            credentials = activations = 0;
            Check(Evaluate(environment).ExitCode == 2 && credentials == 0 && activations == 0, "bad configuration never uses credentials or fallback");
            Check(environment[IntegrationConnectionSettings.ConnectionVariable] == builder.ConnectionString, "bad input unchanged");
        }
        foreach (string? invalid in new string?[] { null, "", "invalid" })
        {
            var environment = Environment(); environment[IntegrationConnectionSettings.ConnectionVariable] = invalid;
            credentials = activations = 0;
            Check(Evaluate(environment).ExitCode == 2 && credentials == 0 && activations == 0, "incomplete supplied input rejected");
        }
        credentials = activations = 0;
        var missing = Evaluate(Environment(), credentialMissing: true);
        Check(missing.ExitCode == 2 && credentials == 1 && activations == 0, "missing Credential Manager record safely stops");
        Check(missing.Findings.All(f => !f.Message.Contains(SyntheticError) && !f.Message.Contains(DummyPassword)), "no exception/secret echo");
        var mismatch = Environment(); var other = Builder(); other.Password = "different-synthetic-placeholder";
        mismatch[IntegrationConnectionSettings.ConnectionVariable] = other.ConnectionString;
        credentials = activations = 0;
        Check(Evaluate(mismatch).ExitCode == 2 && credentials == 1 && activations == 0, "mismatched provided credential does not fall back");
        foreach (bool supplied in new[] { false, true })
        {
            var environment = Environment();
            if (supplied) environment[IntegrationConnectionSettings.ConnectionVariable] = Builder().ConnectionString;
            credentials = activations = 0;
            var result = Evaluate(environment);
            Check(credentials == 1 && activations == 1 && result.ExitCode == 2, "valid fake configuration inspected but limited metadata not PASS");
            Check(!EventDiagnostics.WriteFlags.Any(environment.ContainsKey), "diagnostic does not install write permission");
        }
    }
    private static int credentials, activations;
    private static void MetadataChecks()
    {
        var good = new FakeDatabase(); var report = EventDiagnostics.Inspect(good.Open);
        Check(report.Findings.Count(f => f.Verified) == 4 && report.ExitCode == 2, "visible checks confirmed, completeness NOT VERIFIED");
        Check(good.Opened == 2 && good.Disposed == 2, "independent read-only sessions disposed");
        Check(report.Findings.Any(f => f.Section == "Metadata completeness" && !f.Verified), "empty restricted metadata not proof of absence");
        foreach (var query in Enum.GetValues<DiagnosticQuery>())
        {
            var db = new FakeDatabase { Denied = query };
            var result = EventDiagnostics.Inspect(db.Open);
            Check(result.ExitCode == 2 && result.Findings.Any(f => !f.Verified), "unavailable metadata NOT VERIFIED: " + query);
            Check(result.Findings.All(f => !f.Message.Contains(SyntheticError)), "raw metadata error not exposed");
            Check(db.Opened == db.Disposed, "sessions closed on read error");
        }
        foreach (var target in new object?[][]
        {
            new object?[] { "hotel", "nest_test_runner@localhost", 3306 },
            new object?[] { "nest_integration_test", "root@localhost", 3306 },
            new object?[] { "nest_integration_test", "nest_test_app@localhost", 3306 },
            new object?[] { "nest_integration_test", "nest_test_runner@localhost", 3307 },
            new object?[] { null, "nest_test_runner@localhost", 3306 }
        })
        {
            var db = new FakeDatabase(); db.Rows[DiagnosticQuery.Target] = new[] { target };
            Check(EventDiagnostics.Inspect(db.Open).ExitCode == 2 && db.Reads.SequenceEqual(new[] { DiagnosticQuery.Target }), "wrong actual target stops before other queries");
        }
        foreach (string grant in new[] { "GRANT SELECT ON `hotel`.* TO x", "GRANT ALL PRIVILEGES ON *.* TO x",
            "GRANT PROCESS ON *.* TO x", FakeDatabase.Scoped + " WITH GRANT OPTION", "GRANT TRIGGER ON `nest_integration_test`.* TO x" })
        {
            var db = new FakeDatabase(); db.Rows[DiagnosticQuery.Grants] = new[] { new object?[] { FakeDatabase.Scoped }, new object?[] { grant } };
            Check(EventDiagnostics.Inspect(db.Open).ExitCode == 2 && !db.Reads.Contains(DiagnosticQuery.Engines), "untrusted rights stop before schema");
        }
        foreach (var query in new[] { DiagnosticQuery.Engines, DiagnosticQuery.Columns, DiagnosticQuery.PrimaryKeys, DiagnosticQuery.References, DiagnosticQuery.TicketIndex, DiagnosticQuery.OwnSession })
        {
            var db = new FakeDatabase(); db.Rows[query] = Array.Empty<object?[]>();
            Check(EventDiagnostics.Inspect(db.Open).ExitCode == 2 && EventDiagnostics.Inspect(db.Open).Findings.Any(f => !f.Verified && f.Section != "Metadata completeness"), "missing schema/observation rejected");
        }
        var trigger = new FakeDatabase(); trigger.Rows[DiagnosticQuery.Triggers] = new[] { new object?[] { "visible_test_trigger", "INSERT", "AFTER" } };
        Check(EventDiagnostics.Inspect(trigger.Open).Findings.Any(f => f.Section == "Visible schema" && !f.Verified), "visible trigger not accepted");
        var engine = new FakeDatabase(); engine.Rows[DiagnosticQuery.Engines][0][1] = "MyISAM";
        Check(EventDiagnostics.Inspect(engine.Open).Findings.Any(f => f.Section == "Visible schema" && !f.Verified), "nontransactional engine rejected");
    }
    private static void ReadOnlyChecks()
    {
        foreach (var query in Enum.GetValues<DiagnosticQuery>())
        {
            var sql = EventDiagnostics.QueryText(query);
            Check(sql.StartsWith("SELECT ", StringComparison.Ordinal) || sql == "SHOW GRANTS", "read-only SQL family");
            Check(!Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|REPLACE|CALL|SET|LOCK|UNLOCK)\b", RegexOptions.IgnoreCase), "no write/lock statements");
            Check(!sql.Contains(';') && !sql.Contains("hotel", StringComparison.OrdinalIgnoreCase) && !sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase), "single statement, no hotel or row locks");
        }
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SafetyInputs", "EventDiagnostics.source.txt"));
        foreach (var forbidden in new[] { "ExecuteNonQuery", "BeginTransaction", "EventScenario.", "EventFixture", "EventWorker.", "new EventRepository" })
            Check(!source.Contains(forbidden, StringComparison.Ordinal), "no writing workflow dependency: " + forbidden);
        var changes = Regex.Matches(source, @"Environment\.SetEnvironmentVariable\(([^,\r\n]+),");
        Check(changes.Count == 2 && changes.All(m => m.Groups[1].Value == "IntegrationConnectionSettings.ConnectionVariable"), "only temporary connection configuration, no write flags");
        var entry = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SafetyInputs", "Program.source.txt"));
        Check(entry.IndexOf("EventDiagnostics.Run(args)", StringComparison.Ordinal) < entry.IndexOf("EventDatabaseGuard.ConfigureRunner()", StringComparison.Ordinal), "diagnosis dispatched before writing configuration");
        // Exercise actual entry-point routing with the offline guard; no credential/connection can be reached.
        var main = typeof(Program).GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldOutput = Console.Out;
        try
        {
            using var output = new StringWriter(); Console.SetOut(output);
            var task = (Task<int>)main.Invoke(null, new object[] { new[] { Program.Diagnose, Program.Execute } })!;
            Check(task.GetAwaiter().GetResult() == 2 && output.ToString().Contains("NOT VERIFIED"), "actual mixed-mode entry stops without executing suite");
        }
        finally { Console.SetOut(oldOutput); }
    }

    private sealed class FakeDatabase
    {
        internal const string Scoped = "GRANT SELECT, INSERT, UPDATE, DELETE ON `nest_integration_test`.* TO 'nest_test_runner'@'localhost'";
        internal int Opened, Disposed;
        internal DiagnosticQuery? Denied;
        internal List<DiagnosticQuery> Reads { get; } = new();
        internal Dictionary<DiagnosticQuery, object?[][]> Rows { get; } = new()
        {
            [DiagnosticQuery.Target] = new[] { new object?[] { "nest_integration_test", "nest_test_runner@localhost", 3306 } },
            [DiagnosticQuery.Grants] = new[] { new object?[] { "GRANT USAGE ON *.* TO 'nest_test_runner'@'localhost'" }, new object?[] { Scoped } },
            [DiagnosticQuery.Engines] = new[] { new object?[] { "veranstaltungen", "InnoDB" }, new object?[] { "ticketbestellungen", "InnoDB" } },
            [DiagnosticQuery.Columns] = EventDatabaseGuard.RequiredColumns.Select(p => new object?[]
            {
                p.Key.Split('.')[0], p.Key.Split('.')[1], p.Value, p.Value == "varchar" ? 200L : null, "YES",
                p.Value == "enum" ? "enum('Aktiv','Inaktiv','Ausgebucht')" : p.Value == "decimal" ? "decimal(10,2)" : p.Value,
                p.Key.EndsWith(".id", StringComparison.Ordinal) ? "auto_increment" : ""
            }).ToArray(),
            [DiagnosticQuery.PrimaryKeys] = new[] { new object?[] { "veranstaltungen", "id" }, new object?[] { "ticketbestellungen", "id" } },
            [DiagnosticQuery.References] = new[] { new object?[] { "nest_integration_test", "ticketbestellungen", "veranstaltung_id", "veranstaltungen", "id" } },
            [DiagnosticQuery.TicketIndex] = new[] { new object?[] { "ticket_nummer", 0 } },
            [DiagnosticQuery.Triggers] = Array.Empty<object?[]>(),
            [DiagnosticQuery.OwnSession] = new[] { new object?[] { 1, null } }
        };
        internal IReadOnlyDiagnosticSession Open() { Opened++; return new FakeSession(this, Opened); }
        private sealed class FakeSession(FakeDatabase owner, int id) : IReadOnlyDiagnosticSession
        {
            public int SessionId => id;
            public IReadOnlyList<object?[]> Read(DiagnosticQuery query, int sessionId = 0)
            {
                owner.Reads.Add(query);
                if (owner.Denied == query) throw new InvalidOperationException(SyntheticError);
                return owner.Rows[query];
            }
            public void Dispose() => owner.Disposed++;
        }
    }
}
