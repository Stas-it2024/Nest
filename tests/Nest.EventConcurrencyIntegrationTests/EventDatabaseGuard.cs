using MySql.Data.MySqlClient;
using Nest.Data;
using System.Data;

internal static class EventDatabaseGuard
{
    internal const string WriteVariable = "NEST_ALLOW_EVENT_INTEGRITY_WRITES";
    internal static bool Offline;
    internal static int ConnectionsOpened, CredentialsRead;
    internal static void Authorize() => Safety.Require(
        Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable) == "1" &&
        Environment.GetEnvironmentVariable(WriteVariable) == IntegrationConnectionSettings.DatabaseName);
    internal static void RequireRunner()
    {
        Authorize();
        var settings = IntegrationConnectionSettings.FromEnvironment(requireTestMode: true);
        Safety.Require(settings?.UserName == "nest_test_runner");
    }
    internal static void ConfigureRunner()
    {
        Authorize(); Safety.Require(!Offline);
        // An existing invalid/empty setting is rejected, never replaced by another secret.
        if (!Environment.GetEnvironmentVariables().Contains(IntegrationConnectionSettings.ConnectionVariable))
        {
            CredentialsRead++;
            var builder = new MySqlConnectionStringBuilder
            {
                Server = "localhost", Port = 3306, Database = IntegrationConnectionSettings.DatabaseName,
                UserID = "nest_test_runner", Password = WindowsCredentialReader.ReadPassword("nest_test_runner"), UseAffectedRows = false
            };
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, builder.ConnectionString);
        }
        RequireRunner();
    }
    internal static MySqlConnection Open()
    {
        Safety.Require(!Offline); RequireRunner();
        var conn = DB.GetConnection();
        try
        {
            DB.OpenConnection(conn); ConnectionsOpened++;
            Target(conn);
            using var cmd = Command("SHOW GRANTS", conn);
            using var reader = cmd.ExecuteReader();
            var rows = new List<string>(); while (reader.Read()) rows.Add(reader.GetString(0));
            Safety.Require(IsolatedGrants(rows));
            return conn;
        }
        catch { try { conn.Dispose(); } catch { } throw; }
    }
    internal static bool IsolatedGrants(IEnumerable<string> grants)
    {
        bool found = false;
        foreach (var grant in grants)
        {
            if (grant.Contains("WITH GRANT OPTION", StringComparison.OrdinalIgnoreCase)) return false;
            if (grant.StartsWith("GRANT USAGE ON *.* TO ", StringComparison.Ordinal)) continue;
            const string scope = " ON `nest_integration_test`.* TO ";
            int at = grant.IndexOf(scope, StringComparison.Ordinal);
            if (!grant.StartsWith("GRANT ", StringComparison.Ordinal) || at < 0 ||
                !grant[6..at].Split(", ").Order().SequenceEqual(new[] { "DELETE", "INSERT", "SELECT", "UPDATE" })) return false;
            found = true;
        }
        return found;
    }
    internal static void Target(MySqlConnection conn, MySqlTransaction? tx = null)
    {
        Safety.Require(!Offline); RequireRunner();
        using var cmd = Command("SELECT DATABASE(), CURRENT_USER(), @@port", conn, tx);
        using var reader = cmd.ExecuteReader();
        Safety.Require(reader.Read() && !reader.IsDBNull(0) && reader.GetString(0) == IntegrationConnectionSettings.DatabaseName &&
            reader.GetString(1) == "nest_test_runner@localhost" && reader.GetUInt32(2) == 3306);
    }
    internal static MySqlCommand Command(string sql, MySqlConnection conn, MySqlTransaction? tx = null) => new(sql, conn, tx) { CommandTimeout = 8 };
    internal static int WriteOne(MySqlCommand cmd)
    {
        Target(cmd.Connection, cmd.Transaction);
        Safety.Require(cmd.ExecuteNonQuery() == 1); return checked((int)cmd.LastInsertedId);
    }
    internal static void Schema(MySqlConnection conn)
    {
        using (var cmd = Command("SELECT TABLE_NAME, ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('veranstaltungen','ticketbestellungen')", conn))
        using (var reader = cmd.ExecuteReader())
        {
            var names = new HashSet<string>();
            while (reader.Read()) { Safety.Require(reader.GetString(1) == "InnoDB"); names.Add(reader.GetString(0)); }
            Safety.Require(names.SetEquals(new[] { "veranstaltungen", "ticketbestellungen" }));
        }
        using (var cmd = Command("SELECT TABLE_NAME,COLUMN_NAME,DATA_TYPE,CHARACTER_MAXIMUM_LENGTH,IS_NULLABLE,COLUMN_TYPE,EXTRA FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('veranstaltungen','ticketbestellungen')", conn))
        using (var reader = cmd.ExecuteReader())
        {
            var columns = new Dictionary<string, (string Type, long Length, bool Nullable, string Definition, string Extra)>();
            while (reader.Read()) columns.Add(reader.GetString(0) + "." + reader.GetString(1),
                (reader.GetString(2), reader.IsDBNull(3) ? 0 : reader.GetInt64(3), reader.GetString(4) == "YES", reader.GetString(5), reader.GetString(6)));
            Safety.Require(ColumnsSafe(columns));
        }
        // Reject visible triggers. With DML-only grants, metadata visibility is not proof that
        // no hidden server objects exist; the test DB schema also needs a separate DBA review.
        using (var cmd = Command("SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() AND EVENT_OBJECT_TABLE IN ('veranstaltungen','ticketbestellungen')", conn))
            Safety.Require(Convert.ToInt32(cmd.ExecuteScalar()) == 0);
        using (var cmd = Command("SELECT TABLE_NAME,COLUMN_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('veranstaltungen','ticketbestellungen') AND INDEX_NAME='PRIMARY'", conn))
        using (var reader = cmd.ExecuteReader())
        {
            var keys = new List<string>(); while (reader.Read()) keys.Add(reader.GetString(0) + "." + reader.GetString(1));
            Safety.Require(keys.Order().SequenceEqual(new[] { "ticketbestellungen.id", "veranstaltungen.id" }));
        }
        // No unexpected dependents may be cascaded/removed during fixture cleanup.
        using (var cmd = Command("SELECT TABLE_SCHEMA,TABLE_NAME,COLUMN_NAME,REFERENCED_TABLE_NAME,REFERENCED_COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_SCHEMA=DATABASE() AND REFERENCED_TABLE_NAME IN ('veranstaltungen','ticketbestellungen')", conn))
        using (var reader = cmd.ExecuteReader())
        {
            var refs = new List<string>();
            while (reader.Read()) refs.Add(string.Join("/", Enumerable.Range(0, 5).Select(reader.GetString)));
            Safety.Require(refs.SequenceEqual(new[] { "nest_integration_test/ticketbestellungen/veranstaltung_id/veranstaltungen/id" }));
        }
        using (var cmd = Command("SELECT COLUMN_NAME,NON_UNIQUE FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ticketbestellungen' AND INDEX_NAME IN (SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='ticketbestellungen' AND COLUMN_NAME='ticket_nummer')", conn))
        using (var reader = cmd.ExecuteReader())
        {
            int count = 0;
            while (reader.Read()) { Safety.Require(reader.GetString(0) == "ticket_nummer" && reader.GetInt32(1) == 0); count++; }
            Safety.Require(count == 1);
        }
    }
    internal static readonly Dictionary<string, string> RequiredColumns = new()
    {
        ["veranstaltungen.id"]="int", ["veranstaltungen.titel"]="varchar", ["veranstaltungen.beschreibung"]="text",
        ["veranstaltungen.veranstaltungs_datum"]="date", ["veranstaltungen.veranstaltungs_zeit"]="time",
        ["veranstaltungen.ort"]="varchar", ["veranstaltungen.ticket_preis"]="decimal", ["veranstaltungen.maximale_tickets"]="int",
        ["veranstaltungen.verkaufte_tickets"]="int", ["veranstaltungen.bild_url"]="varchar", ["veranstaltungen.status"]="enum",
        ["veranstaltungen.erstellt_von"]="int", ["veranstaltungen.erstellt_am"]="datetime",
        ["ticketbestellungen.id"]="int", ["ticketbestellungen.veranstaltung_id"]="int", ["ticketbestellungen.gast_name"]="varchar",
        ["ticketbestellungen.gast_email"]="varchar", ["ticketbestellungen.gast_telefon"]="varchar", ["ticketbestellungen.anzahl"]="int",
        ["ticketbestellungen.einzelpreis"]="decimal", ["ticketbestellungen.gesamtpreis"]="decimal", ["ticketbestellungen.ticket_nummer"]="varchar",
        ["ticketbestellungen.reservierung_id"]="int", ["ticketbestellungen.bestellt_am"]="datetime"
    };
    internal static bool ColumnsSafe(IReadOnlyDictionary<string, (string Type, long Length, bool Nullable, string Definition, string Extra)> columns) =>
        columns.Keys.ToHashSet().SetEquals(RequiredColumns.Keys) && RequiredColumns.All(p => columns[p.Key].Type == p.Value) &&
        new[] { "veranstaltungen.id", "ticketbestellungen.id" }.All(k => columns[k].Extra.Contains("auto_increment", StringComparison.Ordinal)) &&
        columns["veranstaltungen.titel"].Length >= 80 && columns["veranstaltungen.bild_url"].Length >= 100 &&
        columns["ticketbestellungen.gast_name"].Length >= 80 && columns["ticketbestellungen.gast_email"].Length >= 80 &&
        columns["ticketbestellungen.gast_telefon"].Length >= 20 && columns["ticketbestellungen.ticket_nummer"].Length >= 25 &&
        columns["veranstaltungen.status"].Definition == "enum('Aktiv','Inaktiv','Ausgebucht')" &&
        new[] { "veranstaltungen.ticket_preis", "ticketbestellungen.einzelpreis", "ticketbestellungen.gesamtpreis" }.All(k => columns[k].Definition == "decimal(10,2)") &&
        new[] { "veranstaltungen.erstellt_von", "ticketbestellungen.reservierung_id" }.All(k => columns[k].Nullable);

    internal static void Preflight(MySqlConnection conn)
    {
        Schema(conn);
        using (var cmd = Command("SELECT VERSION(), @@session.tx_isolation", conn))
        using (var reader = cmd.ExecuteReader()) Safety.Require(reader.Read() && reader.GetString(0).Contains("MariaDB", StringComparison.Ordinal));
        using var observer = Open();
        using var visible = Command("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER='nest_test_runner' AND DB=DATABASE() AND ID=@id", observer);
        visible.Parameters.AddWithValue("@id", conn.ServerThread);
        Safety.Require(Convert.ToInt32(visible.ExecuteScalar()) == 1);
    }
}
