using MySql.Data.MySqlClient;
using Nest.Data;
using System.Data;

internal static class RoomDatabaseGuard
{
    internal const string WriteVariable = "NEST_ALLOW_ROOM_CONCURRENCY_WRITES";
    internal const int CommandTimeout = 8;
    internal static void RequireAuthorization()
    {
        Safety.Require(Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable) == "1" &&
            Environment.GetEnvironmentVariable(WriteVariable) == IntegrationConnectionSettings.DatabaseName);
    }
    internal static void ConfigureRunner()
    {
        RequireAuthorization(); // Before credential retrieval or creating any connection.
        if (!Environment.GetEnvironmentVariables().Contains(IntegrationConnectionSettings.ConnectionVariable))
        {
            var builder = new MySqlConnectionStringBuilder {
                Server = "localhost", Port = 3306, Database = IntegrationConnectionSettings.DatabaseName,
                UserID = "nest_test_runner", Password = WindowsCredentialReader.ReadPassword("nest_test_runner"), UseAffectedRows = false
            };
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, builder.ConnectionString);
        }
        RequireRunner(); // Invalid supplied settings NEVER trigger alternate credentials or fallback.
    }
    internal static void RequireRunner()
    {
        RequireAuthorization();
        var settings = IntegrationConnectionSettings.FromEnvironment(requireTestMode: true);
        Safety.Require(settings?.UserName == "nest_test_runner");
        var parsed = new MySqlConnectionStringBuilder(settings!.ConnectionString);
        Safety.Require(!parsed.UseAffectedRows && !parsed.Pooling && parsed.Server == "localhost" && parsed.Port == 3306);
    }
    internal static MySqlConnection Open()
    {
        RequireRunner();
        var connection = DB.GetConnection();
        try
        {
            DB.OpenConnection(connection);
            AssertTarget(connection);
            using var command = Command("SHOW GRANTS", connection);
            using var reader = command.ExecuteReader();
            var grants = new List<string>();
            while (reader.Read()) grants.Add(reader.GetString(0));
            Safety.Require(GrantsAreIsolated(grants));
            return connection;
        }
        catch { try { connection.Dispose(); } catch { } throw; }
    }
    internal static bool GrantsAreIsolated(IEnumerable<string> grants)
    {
        bool found = false;
        foreach (var grant in grants)
        {
            if (grant.Contains("WITH GRANT OPTION", StringComparison.OrdinalIgnoreCase)) return false;
            if (grant.StartsWith("GRANT USAGE ON *.* TO ", StringComparison.Ordinal)) continue;
            const string scope = " ON `nest_integration_test`.* TO ";
            var at = grant.IndexOf(scope, StringComparison.Ordinal);
            if (!grant.StartsWith("GRANT ", StringComparison.Ordinal) || at < 0 ||
                !grant[6..at].Split(", ").Order().SequenceEqual(new[] { "DELETE", "INSERT", "SELECT", "UPDATE" })) return false;
            found = true;
        }
        return found;
    }
    internal static void AssertTarget(MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        RequireRunner();
        Safety.Require(connection.State == ConnectionState.Open);
        using var command = Command("SELECT DATABASE(), CURRENT_USER(), @@port", connection, transaction);
        using var reader = command.ExecuteReader();
        Safety.Require(reader.Read() && !reader.IsDBNull(0) && reader.GetString(0) == IntegrationConnectionSettings.DatabaseName &&
            reader.GetString(1) == "nest_test_runner@localhost" && reader.GetUInt32(2) == 3306);
    }
    internal static MySqlCommand Command(string sql, MySqlConnection connection, MySqlTransaction? transaction = null) =>
        new(sql, connection, transaction) { CommandTimeout = CommandTimeout };

    internal static int WriteOne(MySqlCommand command)
    {
        AssertTarget(command.Connection ?? throw new InvalidOperationException(), command.Transaction);
        Safety.Require(command.ExecuteNonQuery() == 1);
        return checked((int)command.LastInsertedId);
    }

    internal static void Preflight(MySqlConnection connection)
    {
        using (var version = Command("SELECT VERSION(), @@session.tx_isolation", connection))
        using (var reader = version.ExecuteReader())
        {
            Safety.Require(reader.Read() && reader.GetString(0).StartsWith("10.4.32-MariaDB", StringComparison.Ordinal));
            Console.WriteLine("SERVER version=" + reader.GetString(0) + "; defaultIsolation=" + reader.GetString(1));
        }
        using (var engine = Command("SELECT TABLE_NAME, ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('zimmer','reservierungen')", connection))
        using (var reader = engine.ExecuteReader())
        {
            var tables = new HashSet<string>();
            while (reader.Read()) { Safety.Require(reader.GetString(1) == "InnoDB"); tables.Add(reader.GetString(0)); }
            Safety.Require(tables.SetEquals(new[] { "zimmer", "reservierungen" }));
        }
        using (var keys = Command("SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME = 'zimmer'", connection))
        using (var reader = keys.ExecuteReader())
        {
            var references = new List<string>();
            while (reader.Read()) { Safety.Require(reader.GetString(0) == IntegrationConnectionSettings.DatabaseName); references.Add(reader.GetString(1) + ":" + reader.GetString(2)); }
            Safety.Require(references.SequenceEqual(new[] { "reservierungen:zimmer_id" }));
        }
        using var metadata = Command("SELECT ID, INFO FROM information_schema.PROCESSLIST WHERE USER = @user AND DB = DATABASE() LIMIT 0", connection);
        metadata.Parameters.AddWithValue("@user", "nest_test_runner");
        using (var rows = metadata.ExecuteReader()) { }
        // Confirm cross-session visibility BEFORE creating a fixture; no PROCESS privilege
        // is requested and no application-table read outside the test database is attempted.
        using var observer = Open();
        using var visible = Command("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER = @user AND DB = DATABASE() AND ID = @session", observer);
        visible.Parameters.AddWithValue("@user", "nest_test_runner");
        visible.Parameters.AddWithValue("@session", connection.ServerThread);
        Safety.Require(Convert.ToInt32(visible.ExecuteScalar()) == 1);
    }
}
