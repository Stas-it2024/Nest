using MySql.Data.MySqlClient;
using Nest.Data;
using System.Data;

internal static class B22DatabaseGuard
{
    internal const string WriteVariable = "NEST_ALLOW_B22_WRITES";
    internal static void RequireAuthorization()
    {
        if (Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable) != "1" ||
            Environment.GetEnvironmentVariable(WriteVariable) != IntegrationConnectionSettings.DatabaseName)
            throw new InvalidOperationException("Explicit B2.2 test authorization is required.");
    }

    internal static void ConfigureRunner()
    {
        RequireAuthorization();
        if (!Environment.GetEnvironmentVariables().Contains(IntegrationConnectionSettings.ConnectionVariable))
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
            throw new InvalidOperationException("B2.2 requires the isolated runner account.");
    }

    internal static MySqlConnection Open()
    {
        RequireRunner();
        var connection = DB.GetConnection();
        try
        {
            DB.OpenConnection(connection);
            AssertTarget(connection);
            using var command = TestDatabaseGuard.Command("SHOW GRANTS", connection);
            using var reader = command.ExecuteReader();
            var grants = new List<string>();
            while (reader.Read()) grants.Add(reader.GetString(0));
            // Reuse only the pure grant validator: it neither opens a connection nor requires B2.1 mode.
            if (!TestDatabaseGuard.GrantsAreIsolated(grants)) throw new InvalidOperationException("Runner grants are not isolated.");
            return connection;
        }
        catch { try { connection.Dispose(); } catch { } throw; }
    }

    internal static void AssertTarget(MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        RequireRunner();
        if (connection.State != ConnectionState.Open) throw new InvalidOperationException("Connection is not open.");
        using var command = TestDatabaseGuard.Command("SELECT DATABASE(), CURRENT_USER(), @@port", connection, transaction);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.GetString(0) != IntegrationConnectionSettings.DatabaseName ||
            reader.GetString(1) != "nest_test_runner@localhost" || reader.GetUInt32(2) != 3306)
            throw new InvalidOperationException("Unexpected actual B2.2 target.");
    }

    internal static int WriteOne(MySqlCommand command)
    {
        AssertTarget(command.Connection ?? throw new InvalidOperationException(), command.Transaction);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Unexpected B2.2 fixture write count.");
        return checked((int)command.LastInsertedId);
    }
}
