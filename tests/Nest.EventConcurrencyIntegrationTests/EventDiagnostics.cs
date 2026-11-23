using MySql.Data.MySqlClient;
using Nest.Data;
using System.Globalization;

// This transport has no write/transaction API. Every command is selected from a closed read-only list.
internal enum DiagnosticQuery { Target, Grants, Engines, Columns, PrimaryKeys, References, TicketIndex, Triggers, OwnSession }
internal interface IReadOnlyDiagnosticSession : IDisposable
{
    int SessionId { get; }
    IReadOnlyList<object?[]> Read(DiagnosticQuery query, int sessionId = 0);
}
internal sealed record DiagnosticFinding(string Section, bool Verified, string Message);
internal sealed class DiagnosticReport
{
    internal List<DiagnosticFinding> Findings { get; } = new();
    internal bool Verified => Findings.Count > 0 && Findings.All(f => f.Verified);
    internal int ExitCode => Verified ? 0 : 2;
    internal void Add(string section, bool verified, string message) => Findings.Add(new(section, verified, message));
    internal void Print()
    {
        foreach (var f in Findings) Console.WriteLine($"{f.Section}: {(f.Verified ? "CONFIRMED" : "NOT VERIFIED")}; {f.Message}");
        Console.WriteLine(Verified ? "PASS: read-only diagnostics verified." : "NOT VERIFIED: do not start the eight scenarios until unresolved checks are reviewed.");
    }
}
internal static class EventDiagnostics
{
    internal static int CredentialReads, ConnectionAttempts;
    internal static readonly string[] WriteFlags =
    {
        EventDatabaseGuard.WriteVariable, "NEST_ALLOW_B21_WRITES", "NEST_ALLOW_B22_WRITES", "NEST_ALLOW_ROOM_CONCURRENCY_WRITES"
    };
    internal static IReadOnlyDictionary<string, string?> EnvironmentSnapshot() => Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value as string, StringComparer.OrdinalIgnoreCase);
    internal static void Authorize(IReadOnlyDictionary<string, string?> environment)
    {
        Safety.Require(environment.GetValueOrDefault(IntegrationConnectionSettings.ModeVariable) == "1" &&
            !WriteFlags.Any(environment.ContainsKey)); // Presence, even an empty value, forbids diagnostics.
    }
    internal static string PrepareConfiguration(IReadOnlyDictionary<string, string?> environment, Func<string> readCredential)
    {
        // Validate supplied settings BEFORE touching Credential Manager. Never repair invalid supplied input.
        Authorize(environment);
        string? suppliedPassword = null;
        if (environment.TryGetValue(IntegrationConnectionSettings.ConnectionVariable, out var supplied))
        {
            var checkedSettings = IntegrationConnectionSettings.Parse(supplied ?? "");
            Safety.Require(checkedSettings.UserName == "nest_test_runner");
            suppliedPassword = new MySqlConnectionStringBuilder(checkedSettings.ConnectionString).Password;
        }
        var password = readCredential(); // Only WindowsCredentialReader in the real path; injected dummy in self-tests.
        Safety.Require(!string.IsNullOrEmpty(password) && (suppliedPassword == null || suppliedPassword == password));
        var builder = new MySqlConnectionStringBuilder
        {
            Server = "localhost", Port = 3306, Database = IntegrationConnectionSettings.DatabaseName,
            UserID = "nest_test_runner", Password = password, UseAffectedRows = false
        };
        Safety.Require(IntegrationConnectionSettings.Parse(builder.ConnectionString).UserName == "nest_test_runner");
        // Return the six-key INPUT, not the expanded internal normalized string.
        return builder.ConnectionString;
    }
    internal static DiagnosticReport Evaluate(string[] args, IReadOnlyDictionary<string, string?> environment,
        Func<string> readCredential, Func<Func<IReadOnlyDiagnosticSession>, DiagnosticReport> inspect,
        Func<string, Func<IReadOnlyDiagnosticSession>> activate)
    {
        try
        {
            Safety.Require(Program.Diagnostics(args));
            string configuration = PrepareConfiguration(environment, readCredential);
            return inspect(activate(configuration));
        }
        catch
        {
            var result = new DiagnosticReport();
            result.Add("Configuration/connection", false, "Diagnostic admission, Credential Manager, configuration or connection could not be verified. No fallback or retry.");
            return result; // Never expose exception text (including parser/driver/credential details).
        }
    }
    internal static int Run(string[] args)
    {
        var snapshot = EnvironmentSnapshot();
        bool hadConnection = snapshot.TryGetValue(IntegrationConnectionSettings.ConnectionVariable, out var oldConnection);
        bool activated = false;
        try
        {
            var report = Evaluate(args, snapshot, () =>
            {
                Safety.Require(!EventDatabaseGuard.Offline); CredentialReads++;
                return WindowsCredentialReader.ReadPassword("nest_test_runner");
            }, Inspect, configuration =>
            {
                Safety.Require(!EventDatabaseGuard.Offline);
                // Process scope only; no write authorization is ever installed.
                Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, configuration);
                activated = true;
                return DiagnosticSession.Open;
            });
            report.Print(); return report.ExitCode;
        }
        finally
        {
            if (activated) Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, hadConnection ? oldConnection : null);
        }
    }

    internal static DiagnosticReport Inspect(Func<IReadOnlyDiagnosticSession> open)
    {
        var report = new DiagnosticReport(); string stage = "Connection/target";
        try
        {
            using var connection = open();
            VerifyTarget(connection.Read(DiagnosticQuery.Target));
            report.Add("Target", true, "nest_integration_test; nest_test_runner@localhost; localhost:3306.");
            stage = "Privileges";
            VerifyGrants(connection.Read(DiagnosticQuery.Grants));
            report.Add("Privileges", true, "Only scoped SELECT/INSERT/UPDATE/DELETE and USAGE; no hotel grants, administrative privileges, roles or GRANT OPTION in current SHOW GRANTS.");
            stage = "Visible schema";
            VerifySchema(connection);
            report.Add("Visible schema", true, "Expected InnoDB tables, columns, primary keys, ticket-code uniqueness and visible foreign keys match.");
            // DML-only credentials cannot prove completeness of trigger/cross-schema dependency metadata.
            report.Add("Metadata completeness", false, "No visible triggers/unexpected dependencies found, but restricted metadata visibility cannot prove absence of hidden objects. Separate DBA schema review is required.");
            stage = "Own-session observation";
            using var observer = open();
            VerifyTarget(observer.Read(DiagnosticQuery.Target)); VerifyGrants(observer.Read(DiagnosticQuery.Grants));
            Safety.Require(connection.SessionId > 0 && observer.SessionId > 0 && connection.SessionId != observer.SessionId);
            var rows = observer.Read(DiagnosticQuery.OwnSession, connection.SessionId);
            Safety.Require(rows.Count == 1 && Integer(rows[0][0]) == connection.SessionId);
            report.Add("Own-session observation", true, "A second own connection is visible through PROCESSLIST including INFO access. No lock waits, FIFO order or concurrent API operation was tested.");
            stage = "Session disposal";
        }
        catch { report.Add(stage, false, "Read-only check failed or metadata access was unavailable. No raw server diagnostics are logged; no retry."); }
        return report;
    }
    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private static long Integer(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
    private static void VerifyTarget(IReadOnlyList<object?[]> rows) => Safety.Require(rows.Count == 1 && rows[0].Length == 3 &&
        Text(rows[0][0]) == IntegrationConnectionSettings.DatabaseName && Text(rows[0][1]) == "nest_test_runner@localhost" && Integer(rows[0][2]) == 3306);
    private static void VerifyGrants(IReadOnlyList<object?[]> rows) => Safety.Require(
        rows.All(r => r.Length == 1) && EventDatabaseGuard.IsolatedGrants(rows.Select(r => Text(r[0]))));
    private static void VerifySchema(IReadOnlyDiagnosticSession conn)
    {
        var engines = conn.Read(DiagnosticQuery.Engines);
        Safety.Require(engines.Count == 2 && engines.All(r => Text(r[1]) == "InnoDB") &&
            engines.Select(r => Text(r[0])).ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "veranstaltungen", "ticketbestellungen" }));
        var columns = conn.Read(DiagnosticQuery.Columns).ToDictionary(r => Text(r[0]) + "." + Text(r[1]),
            r => (Type: Text(r[2]), Length: r[3] == null ? 0 : Integer(r[3]), Nullable: Text(r[4]) == "YES", Definition: Text(r[5]), Extra: Text(r[6])));
        Safety.Require(EventDatabaseGuard.ColumnsSafe(columns));
        var primary = conn.Read(DiagnosticQuery.PrimaryKeys).Select(r => Text(r[0]) + "." + Text(r[1])).Order();
        Safety.Require(primary.SequenceEqual(new[] { "ticketbestellungen.id", "veranstaltungen.id" }));
        var refs = conn.Read(DiagnosticQuery.References).Select(r => string.Join("/", r.Select(Text))).ToArray();
        Safety.Require(refs.SequenceEqual(new[] { "nest_integration_test/ticketbestellungen/veranstaltung_id/veranstaltungen/id" }));
        var unique = conn.Read(DiagnosticQuery.TicketIndex);
        Safety.Require(unique.Count == 1 && Text(unique[0][0]) == "ticket_nummer" && Integer(unique[0][1]) == 0);
        Safety.Require(conn.Read(DiagnosticQuery.Triggers).Count == 0);
    }

    internal static string QueryText(DiagnosticQuery query) => query switch
    {
        DiagnosticQuery.Target => "SELECT DATABASE(), CURRENT_USER(), @@port",
        DiagnosticQuery.Grants => "SHOW GRANTS",
        DiagnosticQuery.Engines => "SELECT TABLE_NAME,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('veranstaltungen','ticketbestellungen')",
        DiagnosticQuery.Columns => "SELECT TABLE_NAME,COLUMN_NAME,DATA_TYPE,CHARACTER_MAXIMUM_LENGTH,IS_NULLABLE,COLUMN_TYPE,EXTRA FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('veranstaltungen','ticketbestellungen')",
        DiagnosticQuery.PrimaryKeys => "SELECT TABLE_NAME,COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('veranstaltungen','ticketbestellungen') AND INDEX_NAME='PRIMARY'",
        DiagnosticQuery.References => "SELECT TABLE_SCHEMA,TABLE_NAME,COLUMN_NAME,REFERENCED_TABLE_NAME,REFERENCED_COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_SCHEMA=DATABASE() AND REFERENCED_TABLE_NAME IN ('veranstaltungen','ticketbestellungen')",
        DiagnosticQuery.TicketIndex => "SELECT COLUMN_NAME,NON_UNIQUE FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ticketbestellungen' AND INDEX_NAME IN (SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ticketbestellungen' AND COLUMN_NAME='ticket_nummer')",
        DiagnosticQuery.Triggers => "SELECT TRIGGER_NAME,EVENT_MANIPULATION,ACTION_TIMING FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() AND EVENT_OBJECT_TABLE IN ('veranstaltungen','ticketbestellungen')",
        DiagnosticQuery.OwnSession => "SELECT ID,INFO FROM information_schema.PROCESSLIST WHERE USER='nest_test_runner' AND DB=DATABASE() AND ID=@session",
        _ => throw new ArgumentOutOfRangeException(nameof(query))
    };

    private sealed class DiagnosticSession(MySqlConnection connection) : IReadOnlyDiagnosticSession
    {
        public int SessionId => connection.ServerThread;
        internal static IReadOnlyDiagnosticSession Open()
        {
            Safety.Require(!EventDatabaseGuard.Offline); RequireConfiguration(); ConnectionAttempts++;
            var conn = DB.GetConnection();
            try { DB.OpenConnection(conn); return new DiagnosticSession(conn); }
            catch { try { conn.Dispose(); } catch { } throw; }
        }
        private static void RequireConfiguration()
        {
            Authorize(EnvironmentSnapshot());
            Safety.Require(IntegrationConnectionSettings.FromEnvironment(requireTestMode: true)?.UserName == "nest_test_runner");
        }
        public IReadOnlyList<object?[]> Read(DiagnosticQuery query, int sessionId = 0)
        {
            Safety.Require(!EventDatabaseGuard.Offline); RequireConfiguration();
            using var cmd = new MySqlCommand(QueryText(query), connection) { CommandTimeout = 8 };
            if (query == DiagnosticQuery.OwnSession) { Safety.Require(sessionId > 0); cmd.Parameters.AddWithValue("@session", sessionId); }
            using var reader = cmd.ExecuteReader();
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var values = new object?[reader.FieldCount];
                for (int i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(values);
            }
            return rows;
        }
        public void Dispose() => connection.Dispose();
    }
}
