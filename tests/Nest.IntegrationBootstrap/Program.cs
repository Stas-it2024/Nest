using MySql.Data.MySqlClient;
using Nest.Data;
using System.Data;

internal static class Program
{
    private static int Main(string[] args)
    {
        var originalEnvironment = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
        var originalMode = originalEnvironment[IntegrationConnectionSettings.ModeVariable] as string;
        var originalConnection = originalEnvironment[IntegrationConnectionSettings.ConnectionVariable] as string;
        try
        {
            if (args.Length == 2 && args[0] == "--configuration-lifetime-test")
            {
                ConfigurationTests.RunLifetimeTest(args[1]);
                return 0;
            }
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                ConfigurationTests.Run();
                return 0;
            }
            if (args.Length != 0 || originalMode != "1")
                throw new InvalidOperationException();
            // Validate any supplied configuration instead of silently replacing an invalid one.
            if (originalConnection != null &&
                IntegrationConnectionSettings.FromEnvironment(requireTestMode: true)?.UserName != "nest_test_runner")
                throw new InvalidOperationException();

            // Respect explicitly supplied credentials, including authentication failures; never retry with another secret.
            if (originalConnection == null)
            {
                var builder = new MySqlConnectionStringBuilder
                {
                    Server = "localhost", Port = 3306, Database = IntegrationConnectionSettings.DatabaseName,
                    UserID = "nest_test_runner", Password = WindowsCredentialReader.ReadPassword("nest_test_runner"),
                    UseAffectedRows = false
                };
                Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, builder.ConnectionString);
            }
            if (IntegrationConnectionSettings.FromEnvironment(requireTestMode: true)?.UserName != "nest_test_runner")
                throw new InvalidOperationException();

            using var connection = DB.GetConnection();
            if (connection.State != ConnectionState.Closed)
                throw new InvalidOperationException();
            DB.OpenConnection(connection);
            if (connection.State != ConnectionState.Open)
                throw new InvalidOperationException();
            using (var command = new MySqlCommand("SHOW GRANTS", connection))
            using (var reader = command.ExecuteReader())
            {
                var databaseGrantFound = false;
                while (reader.Read())
                {
                    var grant = reader.GetString(0);
                    // MariaDB can include authentication hashes here. Never print these rows.
                    if (grant.Contains("WITH GRANT OPTION", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException();
                    if (grant.StartsWith("GRANT USAGE ON *.* TO ", StringComparison.Ordinal))
                        continue;
                    const string scope = " ON `nest_integration_test`.* TO ";
                    var scopeIndex = grant.IndexOf(scope, StringComparison.Ordinal);
                    if (!grant.StartsWith("GRANT ", StringComparison.Ordinal) || scopeIndex < 0)
                        throw new InvalidOperationException();
                    var privileges = grant[6..scopeIndex].Split(", ", StringSplitOptions.None).Order().ToArray();
                    if (!privileges.SequenceEqual(new[] { "DELETE", "INSERT", "SELECT", "UPDATE" }))
                        throw new InvalidOperationException();
                    databaseGrantFound = true;
                }
                if (!databaseGrantFound) throw new InvalidOperationException();
            }
            var hotelDenied = false;
            try
            {
                // LIMIT 0 never reads personal records, even if isolation is misconfigured.
                using var command = new MySqlCommand("SELECT 1 FROM hotel.zimmer LIMIT 0", connection);
                using var reader = command.ExecuteReader();
            }
            catch (MySqlException ex) when (ex.Number is 1044 or 1142) { hotelDenied = true; }
            if (!hotelDenied) throw new InvalidOperationException();
            using (var command = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' AND engine = 'InnoDB'", connection))
            {
                if (Convert.ToInt32(command.ExecuteScalar()) != 11) throw new InvalidOperationException();
            }
            Console.WriteLine("PASS: nest_test_runner@localhost; nest_integration_test; 11 InnoDB-Tabellen; hotel-Zugriff verweigert. Keine Schreibabfragen.");
            return 0;
        }
        catch
        {
            // No secrets, authentication hashes or server/driver exception details reach logs.
            Console.Error.WriteLine("FAIL: Testkonfiguration, Zugangsdaten, Verbindung oder Datenbankisolation konnten nicht bestätigt werden. Abbruch ohne Wiederholung.");
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, originalMode);
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, originalConnection);
        }
    }
}
