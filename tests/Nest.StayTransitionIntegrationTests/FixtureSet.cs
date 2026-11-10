using MySql.Data.MySqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;

internal sealed record FixturePlan(string Token, int RoomNumber, DateTime Today)
{
    internal string Marker => "Nest-B21-" + Token;
    internal string CodeA => "B21" + Token[..16] + "A";
    internal string CodeB => "B21" + Token[..16] + "B";
    internal string Email(string suffix) => "b21." + Token + "." + suffix + "@example.invalid";
    internal static FixturePlan Create() => new(Guid.NewGuid().ToString("N"), RandomNumberGenerator.GetInt32(800000000, 900000000), DateTime.Today);
    internal static string ParseToken(string marker)
    {
        if (!marker.StartsWith("Nest-B21-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(marker[9..], "N", out var value) || marker != "Nest-B21-" + value.ToString("N"))
            throw new InvalidOperationException("Invalid fixture marker.");
        return marker[9..];
    }
}

internal sealed class FixtureSet(FixturePlan plan)
{
    internal FixturePlan Plan { get; } = plan;
    internal int RoomId { get; private set; }
    internal int GuestA { get; private set; }
    internal int GuestB { get; private set; }
    internal int BookingA { get; private set; }
    internal int BookingB { get; private set; }
    internal bool Confirmed { get; private set; }
    // Fixture INSERTs only. All stay-transition SQL remains exclusively in the production Repository.
    internal const string InsertRoom = "INSERT INTO zimmer (zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status, beschreibung) VALUES (@number, 'Standard', 2, 50.00, 'Verfuegbar', @marker)";
    internal const string InsertGuest = "INSERT INTO gaeste (vorname, nachname, email, ausweis_typ, ausweis_nummer) VALUES (@first, @marker, @email, 'Sonstiges', @marker)";
    internal const string InsertBooking = "INSERT INTO reservierungen (reservierungs_code, gast_id, zimmer_id, anreise_datum, abreise_datum, naechte, erwachsene, kinder, gesamtbetrag, gezahlter_betrag, zahlungsart, status, sonderwuensche) VALUES (@code, @guest, @room, @arrival, @departure, 1, 1, 0, 50.00, 0.00, 'Bar', 'Reserviert', @marker)";
    internal const string DeleteBooking = "DELETE FROM reservierungen WHERE id = @id AND reservierungs_code = @code AND zimmer_id = @room AND gast_id = @guest AND sonderwuensche = @marker";
    internal const string DeleteRoom = "DELETE FROM zimmer WHERE id = @id AND zimmer_nummer = @number AND beschreibung = @marker";
    internal const string DeleteGuest = "DELETE FROM gaeste WHERE id = @id AND nachname = @marker AND email = @email AND ausweis_nummer = @marker";

    internal void Create()
    {
        using var connection = TestDatabaseGuard.Open();
        using (var schema = TestDatabaseGuard.Command("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' AND engine = 'InnoDB'", connection))
            Require(Convert.ToInt32(schema.ExecuteScalar()) == 11);
        using (var metadata = TestDatabaseGuard.Command("SELECT ID, INFO FROM information_schema.PROCESSLIST WHERE USER = @user AND DB = DATABASE() LIMIT 0", connection))
        {
            metadata.Parameters.AddWithValue("@user", "nest_test_runner");
            using var reader = metadata.ExecuteReader(); // Fail before fixture writes if own-session observation is unavailable.
        }
        using (var foreignKeys = TestDatabaseGuard.Command("SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME = 'reservierungen'", connection))
        using (var reader = foreignKeys.ExecuteReader())
        {
            var references = new List<string>();
            while (reader.Read()) references.Add(reader.GetString(0) + ":" + reader.GetString(1));
            Require(references.Order().SequenceEqual(new[] { "autovermietungen:reservierung_id", "dienstleistungsbestellungen:reservierung_id", "restaurant_reservierungen:reservierung_id", "taxibestellungen:reservierung_id" }));
        }
        using (var collision = TestDatabaseGuard.Command("SELECT COUNT(*) FROM zimmer WHERE zimmer_nummer = @number OR beschreibung = @marker", connection))
        {
            collision.Parameters.AddWithValue("@number", Plan.RoomNumber.ToString(CultureInfo.InvariantCulture));
            collision.Parameters.AddWithValue("@marker", Plan.Marker);
            Require(Convert.ToInt32(collision.ExecuteScalar()) == 0); // Fail, never reuse or retry a collision.
        }
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            using (var room = TestDatabaseGuard.Command(InsertRoom, connection, transaction))
            {
                room.Parameters.AddWithValue("@number", Plan.RoomNumber.ToString(CultureInfo.InvariantCulture));
                room.Parameters.AddWithValue("@marker", Plan.Marker);
                RoomId = TestDatabaseGuard.WriteOne(room); Require(RoomId > 0);
            }
            GuestA = AddGuest(connection, transaction, "A");
            GuestB = AddGuest(connection, transaction, "B");
            BookingA = AddBooking(connection, transaction, Plan.CodeA, GuestA, Plan.Today);
            BookingB = AddBooking(connection, transaction, Plan.CodeB, GuestB, Plan.Today.AddDays(1));
            transaction.Commit();
            Confirmed = true;
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            // If Commit acknowledgement was lost, leave the unique marker for manual diagnosis. No re-import/retry.
            throw;
        }
    }

    private int AddGuest(MySqlConnection connection, MySqlTransaction transaction, string suffix)
    {
        using var command = TestDatabaseGuard.Command(InsertGuest, connection, transaction);
        command.Parameters.AddWithValue("@first", "Integration" + suffix);
        command.Parameters.AddWithValue("@marker", Plan.Marker);
        command.Parameters.AddWithValue("@email", Plan.Email(suffix));
        var id = TestDatabaseGuard.WriteOne(command); Require(id > 0); return id;
    }
    private int AddBooking(MySqlConnection connection, MySqlTransaction transaction, string code, int guest, DateTime arrival)
    {
        using var command = TestDatabaseGuard.Command(InsertBooking, connection, transaction);
        command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@guest", guest);
        command.Parameters.AddWithValue("@room", RoomId); command.Parameters.AddWithValue("@arrival", arrival.Date);
        command.Parameters.AddWithValue("@departure", arrival.AddDays(1).Date); command.Parameters.AddWithValue("@marker", Plan.Marker);
        var id = TestDatabaseGuard.WriteOne(command); Require(id > 0); return id;
    }

    internal static void AssertWorkerOwnership(MySqlConnection connection, string marker, string code, int roomId)
    {
        var token = FixturePlan.ParseToken(marker);
        var expectedA = "B21" + token[..16] + "A"; var expectedB = "B21" + token[..16] + "B";
        Require(code == expectedA || code == expectedB);
        using var command = TestDatabaseGuard.Command("SELECT COUNT(*) FROM reservierungen r JOIN zimmer z ON z.id = r.zimmer_id JOIN gaeste g ON g.id = r.gast_id WHERE r.reservierungs_code = @code AND r.zimmer_id = @room AND r.sonderwuensche = @marker AND z.beschreibung = @marker AND g.nachname = @marker AND g.ausweis_nummer = @marker AND r.status = 'Reserviert'", connection);
        command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@room", roomId); command.Parameters.AddWithValue("@marker", marker);
        Require(roomId > 0 && Convert.ToInt32(command.ExecuteScalar()) == 1);
    }

    internal Dictionary<string, string> ReadOwnedRows(MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        using (var room = TestDatabaseGuard.Command("SELECT zimmer_nummer, beschreibung FROM zimmer WHERE id = @id" + (transaction == null ? "" : " FOR UPDATE"), connection, transaction))
        {
            room.Parameters.AddWithValue("@id", RoomId);
            using var reader = room.ExecuteReader();
            Require(reader.Read() && reader.GetString(0) == Plan.RoomNumber.ToString(CultureInfo.InvariantCulture) && reader.GetString(1) == Plan.Marker);
        }
        using var command = TestDatabaseGuard.Command("SELECT id, reservierungs_code, gast_id, zimmer_id, status, sonderwuensche FROM reservierungen WHERE zimmer_id = @room OR gast_id IN (@a, @b) ORDER BY id" + (transaction == null ? "" : " FOR UPDATE"), connection, transaction);
        command.Parameters.AddWithValue("@room", RoomId); command.Parameters.AddWithValue("@a", GuestA); command.Parameters.AddWithValue("@b", GuestB);
        using var rows = command.ExecuteReader();
        var statuses = new Dictionary<string, string>();
        while (rows.Read())
        {
            var code = rows.GetString(1);
            Require((code == Plan.CodeA && rows.GetInt32(0) == BookingA && rows.GetInt32(2) == GuestA) ||
                    (code == Plan.CodeB && rows.GetInt32(0) == BookingB && rows.GetInt32(2) == GuestB));
            Require(rows.GetInt32(3) == RoomId && rows.GetString(5) == Plan.Marker);
            statuses.Add(code, rows.GetString(4));
        }
        Require(statuses.Count == 2 && GuestA != GuestB && BookingA != BookingB);
        return statuses;
    }

    internal void ReportRetained(string reason)
    {
        Console.WriteLine($"RETAIN/INSPECT {Plan.Marker}; room={RoomId}; guests={GuestA},{GuestB}; bookings={BookingA},{BookingB}; codes={Plan.CodeA},{Plan.CodeB}; setupConfirmed={Confirmed}. {reason}");
    }

    internal void Cleanup(IReadOnlyList<WorkerRunState> workers, bool finalStateVerified)
    {
        // Fail closed before opening a connection. Actual ID/marker ownership is then checked
        // under row locks below, before the first DELETE in the existing cleanup transaction.
        CleanupSafety.RequireAllowed(Confirmed, finalStateVerified, workers);
        Require(RoomId > 0 && GuestA > 0 && GuestB > 0 && GuestA != GuestB &&
            BookingA > 0 && BookingB > 0 && BookingA != BookingB &&
            workers.Select(worker => worker.Code).Order().SequenceEqual(new[] { Plan.CodeA, Plan.CodeB }.Order()));
        FixturePlan.ParseToken(Plan.Marker);
        using var connection = TestDatabaseGuard.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var committed = false;
        try
        {
            // Room-first probe waits for any remaining operation on our rows. No DELETE before locks/ownership checks.
            ReadOwnedRows(connection, transaction);
            foreach (var item in new[] { (GuestA, "A"), (GuestB, "B") })
            {
                using var guest = TestDatabaseGuard.Command("SELECT nachname, email, ausweis_nummer FROM gaeste WHERE id = @id FOR UPDATE", connection, transaction);
                guest.Parameters.AddWithValue("@id", item.Item1);
                using var reader = guest.ExecuteReader();
                Require(reader.Read() && reader.GetString(0) == Plan.Marker && reader.GetString(1) == Plan.Email(item.Item2) && reader.GetString(2) == Plan.Marker);
            }
            // Prevent ON DELETE SET NULL from silently modifying someone else's dependent records.
            foreach (var table in new[] { "autovermietungen", "dienstleistungsbestellungen", "restaurant_reservierungen", "taxibestellungen" })
            {
                using var references = TestDatabaseGuard.Command($"SELECT COUNT(*) FROM {table} WHERE reservierung_id IN (@a, @b)", connection, transaction);
                references.Parameters.AddWithValue("@a", BookingA); references.Parameters.AddWithValue("@b", BookingB);
                Require(Convert.ToInt32(references.ExecuteScalar()) == 0);
            }
            foreach (var row in new[] { (BookingA, Plan.CodeA, GuestA), (BookingB, Plan.CodeB, GuestB) })
            {
                using var command = TestDatabaseGuard.Command(DeleteBooking, connection, transaction);
                command.Parameters.AddWithValue("@id", row.Item1); command.Parameters.AddWithValue("@code", row.Item2);
                command.Parameters.AddWithValue("@room", RoomId); command.Parameters.AddWithValue("@guest", row.Item3); command.Parameters.AddWithValue("@marker", Plan.Marker);
                TestDatabaseGuard.WriteOne(command);
            }
            using (var room = TestDatabaseGuard.Command(DeleteRoom, connection, transaction))
            {
                room.Parameters.AddWithValue("@id", RoomId); room.Parameters.AddWithValue("@number", Plan.RoomNumber.ToString(CultureInfo.InvariantCulture)); room.Parameters.AddWithValue("@marker", Plan.Marker);
                TestDatabaseGuard.WriteOne(room);
            }
            foreach (var item in new[] { (GuestA, "A"), (GuestB, "B") })
            {
                using var guest = TestDatabaseGuard.Command(DeleteGuest, connection, transaction);
                guest.Parameters.AddWithValue("@id", item.Item1); guest.Parameters.AddWithValue("@marker", Plan.Marker); guest.Parameters.AddWithValue("@email", Plan.Email(item.Item2));
                TestDatabaseGuard.WriteOne(guest);
            }
            transaction.Commit();
            committed = true;
            Console.WriteLine("CLEANUP confirmed: only five owned fixture rows deleted.");
        }
        catch
        {
            if (!committed)
                try { transaction.Rollback(); } catch { }
            throw; // No cleanup retry, including after uncertain Commit.
        }
    }
    internal static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Fixture ownership or test invariant could not be confirmed.");
    }
}
