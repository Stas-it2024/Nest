using MySql.Data.MySqlClient;
using Nest.Data;
using System.Data;

internal static class TestDatabaseGuard
{
    internal const string WriteVariable = "NEST_ALLOW_B21_WRITES";
    internal const int CommandTimeout = 8;

    internal static void RequireAuthorization()
    {
        if (Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable) != "1" ||
            Environment.GetEnvironmentVariable(WriteVariable) != IntegrationConnectionSettings.DatabaseName)
            throw new InvalidOperationException("Explicit B2.1 write authorization is required.");
    }

    internal static void ConfigureRunner()
    {
        RequireAuthorization();
        var environment = Environment.GetEnvironmentVariables();
        if (!environment.Contains(IntegrationConnectionSettings.ConnectionVariable))
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = "localhost", Port = 3306, Database = IntegrationConnectionSettings.DatabaseName,
                UserID = "nest_test_runner", Password = WindowsCredentialReader.ReadPassword("nest_test_runner"),
                UseAffectedRows = false
            };
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, builder.ConnectionString);
        }
        RequireRunner();
    }

    private static void RequireRunner()
    {
        RequireAuthorization();
        if (IntegrationConnectionSettings.FromEnvironment(requireTestMode: true)?.UserName != "nest_test_runner")
            throw new InvalidOperationException("Only the restricted runner account is allowed.");
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
            if (!GrantsAreIsolated(grants)) throw new InvalidOperationException("Runner privileges are not isolated.");
            return connection;
        }
        catch
        {
            try { connection.Dispose(); } catch { }
            throw;
        }
    }

    // No query is sent to hotel. Restrict privileges by examining grants, not by reading its tables.
    internal static bool GrantsAreIsolated(IEnumerable<string> grants)
    {
        var databaseGrant = false;
        foreach (var grant in grants)
        {
            if (grant.Contains("WITH GRANT OPTION", StringComparison.OrdinalIgnoreCase)) return false;
            if (grant.StartsWith("GRANT USAGE ON *.* TO ", StringComparison.Ordinal)) continue;
            const string scope = " ON `nest_integration_test`.* TO ";
            var index = grant.IndexOf(scope, StringComparison.Ordinal);
            if (!grant.StartsWith("GRANT ", StringComparison.Ordinal) || index < 0) return false;
            if (!grant[6..index].Split(", ").Order().SequenceEqual(new[] { "DELETE", "INSERT", "SELECT", "UPDATE" })) return false;
            databaseGrant = true;
        }
        return databaseGrant;
    }

    internal static void AssertTarget(MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        RequireRunner();
        if (connection.State != ConnectionState.Open) throw new InvalidOperationException("Connection is not open.");
        using var command = Command("SELECT DATABASE(), CURRENT_USER(), @@port", connection, transaction);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.GetString(0) != IntegrationConnectionSettings.DatabaseName ||
            reader.GetString(1) != "nest_test_runner@localhost" || reader.GetUInt32(2) != 3306)
            throw new InvalidOperationException("Unexpected actual database or account.");
    }

    internal static MySqlCommand Command(string sql, MySqlConnection connection, MySqlTransaction? transaction = null) =>
        new(sql, connection, transaction) { CommandTimeout = CommandTimeout };

    internal static int WriteOne(MySqlCommand command)
    {
        var connection = command.Connection ?? throw new InvalidOperationException();
        AssertTarget(connection, command.Transaction);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Unexpected fixture write count.");
        return checked((int)command.LastInsertedId);
    }
}
