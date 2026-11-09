using MySql.Data.MySqlClient;
using System.Data;
using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Data
{
    public static class DB
    {
        private sealed record ConnectionContext(string Configuration, string? TestUser);
        private static readonly ConditionalWeakTable<MySqlConnection, ConnectionContext> Contexts = new();
        private static readonly object ConfigurationGate = new();
        private static ConnectionContext? ProcessConfiguration;
        // Eine zentrale Verbindungszeichenfolge verhindert unterschiedliche Datenbankeinstellungen in den Repositories.
        private const string ConnectionString = "UseAffectedRows=false;" +
            "server=localhost;port=3306;database=hotel;uid=root;pwd=root;SslMode=Disabled;";

        public static MySqlConnection GetConnection()
        {
            // Jeder Aufruf liefert eine neue Verbindung, deren Lebensdauer das aufrufende Repository kontrolliert.
            var context = GetProcessConfiguration();
            var connection = new MySqlConnection(context.Configuration);
            Contexts.Add(connection, context);
            return connection;
        }

        private static ConnectionContext GetProcessConfiguration()
        {
            lock (ConfigurationGate)
            {
                var settings = IntegrationConnectionSettings.FromEnvironment();
                var current = new ConnectionContext(settings?.ConnectionString ?? ConnectionString, settings?.UserName);
                if (ProcessConfiguration == null)
                    ProcessConfiguration = current;
                else if (ProcessConfiguration != current)
                    throw new InvalidOperationException("Die Verbindungskonfiguration darf während der Prozesslaufzeit nicht geändert werden.");
                return ProcessConfiguration;
            }
        }

        // All application callers use this method instead of Open(), so validation precedes their SQL.
        public static void OpenConnection(MySqlConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (connection.State != ConnectionState.Closed || !Contexts.TryGetValue(connection, out var context))
                throw new InvalidOperationException("Die Verbindung muss geschlossen und über DB.GetConnection erstellt sein.");

            try
            {
                if (context != GetProcessConfiguration() ||
                    !string.Equals(connection.ConnectionString, context.Configuration, StringComparison.Ordinal))
                    throw new InvalidOperationException("Die Verbindungskonfiguration wurde nach ihrer Erstellung geändert.");

                connection.Open();
                if (context.TestUser == null)
                    return;

                using var command = new MySqlCommand("SELECT DATABASE(), CURRENT_USER(), @@port", connection);
                using var reader = command.ExecuteReader();
                if (!reader.Read() || reader.IsDBNull(0) ||
                    reader.GetString(0) != IntegrationConnectionSettings.DatabaseName ||
                    reader.GetString(1) != context.TestUser + "@localhost" || reader.GetUInt32(2) != 3306)
                    throw new InvalidOperationException("Die tatsächliche Testverbindung ist nicht zulässig.");
            }
            catch
            {
                try { connection.Close(); } catch { /* Preserve the original failure. */ }
                try { connection.Dispose(); } catch { /* Cleanup must not hide the original failure. */ }
                throw;
            }
        }
    }
}
