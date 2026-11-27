using System.Collections.Concurrent;
using System.Data;
using MySql.Data.MySqlClient;
using Nest.Data;
using Nest.Data.Authentication;

internal sealed class AccountDatabaseGuard : IDisposable
{
    internal const string ExecuteArgument = "--execute-account-store";
    internal const string WriteVariable = "NEST_ALLOW_ACCOUNT_STORE_WRITES";
    internal const string MetadataVariable = "NEST_ACCOUNT_STORE_METADATA_VERIFIED";
    internal const string Database = IntegrationConnectionSettings.DatabaseName;
    internal const string Runner = "nest_test_runner";
    private static bool offline;
    internal static int CredentialCalls, ConnectionAttempts, StoreCreations;
    private readonly string? priorConnection;
    private readonly ConcurrentDictionary<long, byte> connections = new();
    private bool disposed, preflightPassed;
    private AccountDatabaseGuard(string? previous) { priorConnection = previous; }
    internal static bool IsOffline => offline;
    internal static void SealOffline() => offline = true;
    internal static void ReportFailure(Exception error)
    {
        Console.WriteLine("Failure category=" + error.GetType().Name +
            (error is MySqlException mysql ? "; driverCode=" + mysql.Number : ""));
    }
    internal static void Require(bool value)
    { if (!value) throw new InvalidOperationException("Account integration safety condition failed."); }

    internal static void Authorize(string[] args, IReadOnlyDictionary<string, string?> env)
    {
        Require(args.SequenceEqual(new[] { ExecuteArgument }));
        Require(env.GetValueOrDefault(IntegrationConnectionSettings.ModeVariable) == "1");
        Require(env.GetValueOrDefault(WriteVariable) == Database);
        // Explicit acknowledgement of an independent DBA inspection: limited grants cannot
        // prove absence of hidden triggers/cross-schema dependencies in metadata.
        Require(env.GetValueOrDefault(MetadataVariable) == Database);
        Require(!env.ContainsKey(IntegrationConnectionSettings.ConnectionVariable));
        Require(!env.Any(x => x.Key.StartsWith("NEST_ALLOW_", StringComparison.OrdinalIgnoreCase) &&
            x.Key.EndsWith("_WRITES", StringComparison.OrdinalIgnoreCase) && x.Key != WriteVariable));
        // Validate the fixed target with the shared parser BEFORE consulting Credential Manager.
        ValidateTarget("localhost", 3306, Database, Runner);
    }
    internal static void ValidateTarget(string host, uint port, string database, string user)
    {
        var value = new MySqlConnectionStringBuilder { Server = host, Port = port, Database = database,
            UserID = user, Password = "synthetic-preflight-placeholder", UseAffectedRows = false };
        Require(IntegrationConnectionSettings.Parse(value.ConnectionString).UserName == Runner);
    }
    internal static AccountDatabaseGuard Configure(string[] args, IReadOnlyDictionary<string, string?> env,
        Func<string, string> readCredential)
    {
        Authorize(args, env);
        Require(!offline);
        Interlocked.Increment(ref CredentialCalls);
        var password = readCredential(Runner); // Existing WindowsCredentialReader, never an environment password.
        Require(!string.IsNullOrEmpty(password));
        var builder = new MySqlConnectionStringBuilder { Server = "localhost", Port = 3306, Database = Database,
            UserID = Runner, Password = password, UseAffectedRows = false };
        var parsed = IntegrationConnectionSettings.Parse(builder.ConnectionString);
        Require(parsed.UserName == Runner);
        // Process only, restored at exit. No system/user environment or project configuration writes.
        var previous = Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable);
        Require(previous is null && Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable) == "1");
        Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, builder.ConnectionString);
        return new AccountDatabaseGuard(previous);
    }
    private void Ready()
    {
        Require(!offline && !disposed);
        Require(Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable) == "1" &&
            Environment.GetEnvironmentVariable(WriteVariable) == Database &&
            Environment.GetEnvironmentVariable(MetadataVariable) == Database);
        Require(IntegrationConnectionSettings.FromEnvironment(requireTestMode: true)?.UserName == Runner);
    }
    internal SqlAccountStore Store(Action<long>? connectionOpened = null)
    {
        Ready(); Require(preflightPassed); Interlocked.Increment(ref StoreCreations);
        return new SqlAccountStore(() => Open(connectionOpened));
    }
    internal AccountStoreSession Open(Action<long>? connectionOpened = null)
    {
        Ready();
        var session = AccountStoreSession.Open(() => DB.GetAccountStoreConnection(), c =>
        {
            Ready(); Interlocked.Increment(ref ConnectionAttempts);
            DB.OpenConnection((MySqlConnection)c); // Registered configuration + actual database/user/port.
        });
        try
        {
            var rows = session.Read("SELECT DATABASE() AS db, CURRENT_USER() AS who, @@port AS port, CONNECTION_ID() AS cid");
            Require(rows.Count == 1 && Text(rows[0], "db") == Database &&
                Text(rows[0], "who") == Runner + "@localhost" && Number(rows[0], "port") == 3306);
            var id = Number(rows[0], "cid"); connections.TryAdd(id, 0); connectionOpened?.Invoke(id);
            return session;
        }
        catch { session.Close(); throw; }
    }
    internal static long Number(Dictionary<string, object?> row, string key) => AccountStoreSchemaGuard.Number(row, key);
    internal static string? Text(Dictionary<string, object?> row, string key) => AccountStoreSchemaGuard.Text(row, key);
    internal static bool IsolatedGrants(IEnumerable<string> grants)
    {
        var found = false;
        foreach (var grant in grants)
        {
            if (grant.Contains("WITH GRANT OPTION", StringComparison.OrdinalIgnoreCase)) return false;
            if (grant.StartsWith("GRANT USAGE ON *.* TO ", StringComparison.Ordinal)) continue;
            const string scope = " ON `nest_integration_test`.* TO ";
            var at = grant.IndexOf(scope, StringComparison.Ordinal);
            if (!grant.StartsWith("GRANT ", StringComparison.Ordinal) || at < 0 ||
                !grant[6..at].Split(", ").Order(StringComparer.Ordinal)
                    .SequenceEqual(new[] { "DELETE", "INSERT", "SELECT", "UPDATE" })) return false;
            found = true;
        }
        return found;
    }
    internal HashSet<int> Preflight()
    {
        var s = Open();
        try
        {
            var grants = s.Read("SHOW GRANTS");
            Require(IsolatedGrants(grants.Select(r => (string)r.Values.Single()!)));
            // Full schema and all three complete CHECK expressions already verified by the production guard.
            var state = s.Read("SELECT VERSION() AS version, @@session.sql_mode AS mode, @@session.check_constraint_checks AS checks");
            Require(Text(state.Single(), "version")!.Contains("MariaDB", StringComparison.Ordinal) &&
                Text(state.Single(), "mode")!.Split(',').Contains("STRICT_ALL_TABLES", StringComparer.OrdinalIgnoreCase) &&
                Number(state.Single(), "checks") == 1);
            Require(Number(s.Read("SELECT COUNT(*) AS n FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() AND EVENT_OBJECT_TABLE='benutzer'").Single(), "n") == 0);
            Require(Number(s.Read("SELECT COUNT(*) AS n FROM information_schema.KEY_COLUMN_USAGE WHERE (TABLE_SCHEMA=DATABASE() AND TABLE_NAME='benutzer' AND REFERENCED_TABLE_NAME IS NOT NULL) OR (REFERENCED_TABLE_SCHEMA=DATABASE() AND REFERENCED_TABLE_NAME='benutzer')").Single(), "n") == 0);
            Require(Number(s.Read("SELECT COUNT(*) AS n FROM information_schema.EVENTS WHERE EVENT_SCHEMA=DATABASE() AND STATUS='ENABLED'").Single(), "n") == 0);
            // Do not select names/passwords/profile fields of the three original accounts.
            Require(Text(s.Read("SELECT @@global.event_scheduler AS scheduler").Single(), "scheduler") == "OFF");
            var ids = s.Read("SELECT id FROM benutzer").Select(r => checked((int)Number(r, "id"))).ToHashSet();
            Require(ids.Count == 3 && ids.All(id => id > 0));
            Require(s.Read("SELECT ID FROM information_schema.PROCESSLIST WHERE USER='nest_test_runner' AND DB=DATABASE() AND ID=CONNECTION_ID()").Count == 1);
            Console.WriteLine("PREFLIGHT PASS: test database/user/port; limited DML grants; complete production schema guard; original IDs=3.");
            Console.WriteLine("Metadata completeness relies additionally on the explicit independent DBA attestation.");
            preflightPassed = true; return ids;
        }
        finally { Require(!s.Close()); }
    }
    internal async Task AssertConnectionsReleasedAsync()
    {
        var s = Open();
        try
        {
            var own = Number(s.Read("SELECT CONNECTION_ID() AS cid").Single(), "cid");
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (true)
            {
                var live = false;
                foreach (var id in connections.Keys.Where(x => x != own))
                    live |= s.Read("SELECT ID FROM information_schema.PROCESSLIST WHERE USER='nest_test_runner' AND ID=@id",
                        ("@id", DbType.Int64, id)).Count != 0;
                if (!live) return;
                Require(DateTime.UtcNow < deadline);
                await Task.Delay(25);
            }
        }
        finally { Require(!s.Close()); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, priorConnection);
    }
}