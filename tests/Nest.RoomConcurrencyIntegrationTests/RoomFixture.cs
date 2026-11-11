using MySql.Data.MySqlClient;
using Nest.Models;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;

internal sealed record RoomPlan(string Token, int Number)
{
    internal string Marker => "Nest-RoomCC-" + Token;
    internal string Owner => "NestRoomCC/" + Token + ".png";
    internal string Description(string key) => Marker + ":" + key;
    internal static RoomPlan New() => new(Guid.NewGuid().ToString("N"), RandomNumberGenerator.GetInt32(900000000, 1000000000));
    internal static RoomPlan Parse(string marker, string number)
    {
        const string prefix = "Nest-RoomCC-";
        Safety.Require(marker.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(marker[prefix.Length..], "N", out _));
        var token = marker[prefix.Length..];
        Safety.Require(marker == prefix + Guid.ParseExact(token, "N").ToString("N"));
        var value = int.Parse(number, CultureInfo.InvariantCulture);
        Safety.Require(value >= 900000000 && value < 1000000000 && number == value.ToString(CultureInfo.InvariantCulture));
        return new(token, value);
    }
}

internal sealed class RoomFixture(RoomPlan plan)
{
    internal RoomPlan Plan { get; } = plan;
    internal int Id { get; private set; }
    internal bool CreatedConfirmed { get; private set; }
    internal RoomOriginalValues? Initial { get; private set; }
    internal bool CleanupConfirmed { get; private set; }
    internal const string InsertSql = "INSERT INTO zimmer (zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status, beschreibung, bild_pfad, hinzugefuegt_am) VALUES (@number, 'Standard', 1, 50.25, 'Verfuegbar', @description, @owner, @added)";
    internal const string DeleteSql = "DELETE FROM zimmer WHERE id = @id AND zimmer_nummer = @number AND BINARY bild_pfad = BINARY @owner AND BINARY beschreibung = BINARY @description";
    private const string Projection = "SELECT id, zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status, beschreibung, bild_pfad, hinzugefuegt_am, geloescht_am FROM zimmer WHERE id = @id";

    internal void Create()
    {
        using var connection = RoomDatabaseGuard.Open();
        using (var collision = RoomDatabaseGuard.Command("SELECT COUNT(*) FROM zimmer WHERE zimmer_nummer = @number OR bild_pfad = @owner OR beschreibung IN (@original, @a, @b)", connection))
        {
            collision.Parameters.AddWithValue("@number", Plan.Number.ToString(CultureInfo.InvariantCulture));
            collision.Parameters.AddWithValue("@owner", Plan.Owner);
            collision.Parameters.AddWithValue("@original", Plan.Description("original"));
            collision.Parameters.AddWithValue("@a", Plan.Description("A")); collision.Parameters.AddWithValue("@b", Plan.Description("B"));
            Safety.Require(Convert.ToInt32(collision.ExecuteScalar()) == 0); // Never reuse or retry a collision.
        }
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        bool committed = false;
        try
        {
            using (var command = RoomDatabaseGuard.Command(InsertSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@number", Plan.Number.ToString(CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@description", Plan.Description("original"));
                command.Parameters.AddWithValue("@owner", Plan.Owner); command.Parameters.AddWithValue("@added", DateTime.Today);
                Id = RoomDatabaseGuard.WriteOne(command);
                Safety.Require(Id > 0);
            }
            transaction.Commit(); committed = true; CreatedConfirmed = true;
        }
        catch
        {
            if (!committed) try { transaction.Rollback(); } catch { }
            throw; // No creation retry or cleanup after an unacknowledged Commit.
        }
        Initial = ReadOwned(connection, Plan, Id);
        Safety.Require(Initial.Description == Plan.Description("original") && Initial.PricePerNight == 50.25m && Initial.DateDelete == null);
    }

    internal static bool IsOwned(RoomOriginalValues row, RoomPlan plan, int id) =>
        id > 0 && row.Id == id && row.RoomNumber == plan.Number.ToString(CultureInfo.InvariantCulture) && row.ImagePath == plan.Owner &&
        row.Type == "Standard" && row.Capacity == 1 && row.Status == "Verfuegbar" && row.DateAdded != null &&
        new[] { plan.Description("original"), plan.Description("A"), plan.Description("B") }.Contains(row.Description, StringComparer.Ordinal);

    internal static RoomOriginalValues ReadOwned(MySqlConnection connection, RoomPlan plan, int id, MySqlTransaction? transaction = null)
    {
        using var command = RoomDatabaseGuard.Command(Projection + (transaction == null ? "" : " FOR UPDATE"), connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader();
        Safety.Require(reader.Read());
        var row = new RoomOriginalValues(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetDecimal(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            reader.IsDBNull(9) ? null : reader.GetDateTime(9));
        Safety.Require(!reader.Read() && IsOwned(row, plan, id));
        return row;
    }
    internal static void AssertNoDependencies(MySqlConnection connection, int id, MySqlTransaction? transaction = null)
    {
        using var command = RoomDatabaseGuard.Command("SELECT id FROM reservierungen WHERE zimmer_id = @id" + (transaction == null ? "" : " FOR UPDATE"), connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        using var reader = command.ExecuteReader(); Safety.Require(!reader.Read());
    }
    internal void Cleanup(RoomOriginalValues verified, IReadOnlyList<RoomWorkerState> workers, bool finalVerified, int expectedCount)
    {
        Safety.Require(RoomWorkerState.CanCleanup(CreatedConfirmed, finalVerified, workers, expectedCount));
        RoomPlan.Parse(Plan.Marker, Plan.Number.ToString(CultureInfo.InvariantCulture));
        using var connection = RoomDatabaseGuard.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        bool committed = false;
        try
        {
            // Reacquiring the parent row also probes lock release. Its X lock protects
            // the FK parent while checking dependencies, before any physical DELETE.
            var current = ReadOwned(connection, Plan, Id, transaction);
            Safety.Require(current == verified);
            AssertNoDependencies(connection, Id, transaction);
            using (var command = RoomDatabaseGuard.Command(DeleteSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@id", Id); command.Parameters.AddWithValue("@number", current.RoomNumber);
                command.Parameters.AddWithValue("@owner", Plan.Owner); command.Parameters.AddWithValue("@description", current.Description);
                RoomDatabaseGuard.WriteOne(command);
            }
            transaction.Commit(); committed = true;
        }
        catch
        {
            if (!committed) try { transaction.Rollback(); } catch { }
            throw; // In particular: no second DELETE after uncertain cleanup Commit.
        }
        using var absence = RoomDatabaseGuard.Command("SELECT COUNT(*) FROM zimmer WHERE id = @id OR bild_pfad = @owner OR beschreibung IN (@original, @a, @b)", connection);
        absence.Parameters.AddWithValue("@id", Id); absence.Parameters.AddWithValue("@owner", Plan.Owner);
        absence.Parameters.AddWithValue("@original", Plan.Description("original"));
        absence.Parameters.AddWithValue("@a", Plan.Description("A")); absence.Parameters.AddWithValue("@b", Plan.Description("B"));
        Safety.Require(Convert.ToInt32(absence.ExecuteScalar()) == 0);
        CleanupConfirmed = true;
        Console.WriteLine($"CLEANUP confirmed marker={Plan.Marker}; room={Id}; no own ID/markers remain.");
    }
    internal void ReportRetained() => Console.WriteLine($"RETAIN/INSPECT marker={Plan.Marker}; room={Id}; number={Plan.Number}; creationConfirmed={CreatedConfirmed}; cleanupConfirmed={CleanupConfirmed}. No automatic retry.");
}
