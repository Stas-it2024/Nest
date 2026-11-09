using MySql.Data.MySqlClient;
using System;
using System.Data.Common;
using System.Linq;

namespace Nest.Data
{
    public sealed class IntegrationConnectionSettings
    {
        public const string ModeVariable = "NEST_INTEGRATION_TEST";
        public const string ConnectionVariable = "NEST_INTEGRATION_CONNECTION_STRING";
        public const string DatabaseName = "nest_integration_test";
        public string UserName { get; }
        public string ConnectionString { get; }

        private IntegrationConnectionSettings(string userName, string connectionString)
        {
            UserName = userName;
            ConnectionString = connectionString;
        }

        // A supplied but incomplete/invalid test configuration never selects the normal database.
        public static IntegrationConnectionSettings? FromEnvironment(bool requireTestMode = false)
        {
            // Enumerate one snapshot: GetEnvironmentVariable can conflate empty and absent values.
            var environment = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
            var mode = environment[ModeVariable] as string;
            var value = environment[ConnectionVariable] as string;
            if (!environment.Contains(ModeVariable) && !environment.Contains(ConnectionVariable) && !requireTestMode)
                return null;
            if (mode != "1" || string.IsNullOrEmpty(value))
                throw ConfigurationError();

            return Parse(value);
        }

        public static IntegrationConnectionSettings Parse(string value)
        {
            try
            {
                var raw = new DbConnectionStringBuilder { ConnectionString = value };
                string[][] groups =
                {
                    new[] { "server", "host", "data source", "datasource", "address", "addr", "network address" },
                    new[] { "port" }, new[] { "database", "initial catalog" },
                    new[] { "user id", "userid", "uid", "user name", "username" },
                    new[] { "password", "pwd" }, new[] { "useaffectedrows", "use affected rows" }
                };
                var keys = raw.Keys.Cast<string>().ToArray();
                if (groups.Any(group => keys.Count(key => group.Contains(key, StringComparer.OrdinalIgnoreCase)) != 1) ||
                    keys.Any(key => !groups.Any(group => group.Contains(key, StringComparer.OrdinalIgnoreCase))))
                    throw ConfigurationError();

                var parsed = new MySqlConnectionStringBuilder(value);
                if (!string.Equals(parsed.Server, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    parsed.Port != 3306 || parsed.Database != DatabaseName || parsed.UseAffectedRows ||
                    (parsed.UserID != "nest_test_app" && parsed.UserID != "nest_test_runner") ||
                    string.IsNullOrEmpty(parsed.Password))
                    throw ConfigurationError();

                // Rebuild from approved settings only: no pipes, failover hosts, init commands or pooling.
                var safe = new MySqlConnectionStringBuilder
                {
                    Server = "localhost", Port = 3306, Database = DatabaseName,
                    UserID = parsed.UserID, Password = parsed.Password, UseAffectedRows = false,
                    ConnectionProtocol = MySqlConnectionProtocol.Sockets,
                    Pooling = false, PersistSecurityInfo = false
                };
                return new IntegrationConnectionSettings(parsed.UserID, safe.ConnectionString);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                // Do not include the supplied string or a parser exception that could contain secrets.
                throw ConfigurationError();
            }
        }

        private static InvalidOperationException ConfigurationError() => new(
            "Ungültige Testkonfiguration. Expliziter Testmodus und vollständige lokale Testzugangsdaten sind erforderlich.");
    }
}
