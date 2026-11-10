using MySql.Data.MySqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;

internal sealed record B22FixturePlan(string Token, int RoomNumber, DateTime Today)
{
    internal string Marker => "Nest-B22-" + Token;
    internal string Code => "B22" + Token[..17];
    internal string Email => "b22." + Token + "@example.invalid";
    internal static B22FixturePlan Create() => new(Guid.NewGuid().ToString("N"), RandomNumberGenerator.GetInt32(900000000, 1000000000), DateTime.Today);
    internal static void ValidateWorker(string marker, string code, int roomId)
    {
        if (!marker.StartsWith("Nest-B22-", StringComparison.Ordinal) || !Guid.TryParseExact(marker[9..], "N", out var token) ||
            marker != "Nest-B22-" + token.ToString("N") || code != "B22" + marker[9..26] || roomId <= 0)
            throw new InvalidOperationException("Invalid B2.2 fixture identity.");
    }
}

internal sealed class B22FixtureSet(B22FixturePlan plan)
{
    internal B22FixturePlan Plan { get; } = plan;
    internal int RoomId { get; private set; }
    internal int GuestId { get; private set; }
    internal int BookingId { get; private set; }
    internal bool Confirmed { get; private set; }

    internal void Create()
    {
        using var connection = B22DatabaseGuard.Open();
        using (var schema = TestDatabaseGuard.Command("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' AND engine = 'InnoDB'", connection))
            FixtureSet.Require(Convert.ToInt32(schema.ExecuteScalar()) == 11);
        using (var metadata = TestDatabaseGuard.Command("SELECT ID, INFO FROM information_schema.PROCESSLIST WHERE USER = @user AND DB = DATABASE() LIMIT 0", connection))
        {
            metadata.Parameters.AddWithValue("@user", "nest_test_runner");
            using var reader = metadata.ExecuteReader();
        }
        using (var keys = TestDatabaseGuard.Command("SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IN ('reservierungen', 'zimmer', 'gaeste')", connection))
        using (var reader = keys.ExecuteReader())
        {
            var actual = new List<string>();
            while (reader.Read()) actual.Add(reader.GetString(0) + ":" + reader.GetString(1) + ":" + reader.GetString(2));
            FixtureSet.Require(actual.Order().SequenceEqual(new[]
            {
                "autovermietungen:reservierung_id:reservierungen", "dienstleistungsbestellungen:reservierung_id:reservierungen",
                "reservierungen:gast_id:gaeste", "reservierungen:zimmer_id:zimmer",
                "restaurant_reservierungen:reservierung_id:reservierungen", "taxibestellungen:reservierung_id:reservierungen"
            }.Order()));
        }
        foreach (var sql in new[]
        {
            "SELECT COUNT(*) FROM zimmer WHERE zimmer_nummer = @number OR beschreibung = @marker",
            "SELECT COUNT(*) FROM gaeste WHERE email = @email OR ausweis_nummer = @marker",
            "SELECT COUNT(*) FROM reservierungen WHERE reservierungs_code = @code OR sonderwuensche = @marker"
        })
        {
            using var collision = TestDatabaseGuard.Command(sql, connection);
            collision.Parameters.AddWithValue("@number", Plan.RoomNumber.ToString(CultureInfo.InvariantCulture));
            collision.Parameters.AddWithValue("@marker", Plan.Marker); collision.Parameters.AddWithValue("@email", Plan.Email);
            collision.Parameters.AddWithValue("@code", Plan.Code);
            FixtureSet.Require(Convert.ToInt32(collision.ExecuteScalar()) == 0); // Never reuse/retry a collision.
        }
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            // Reuse only B2.1 fixture SQL. No Repository business SQL is copied or executed here.
            using (var room = TestDatabaseGuard.Command(FixtureSet.InsertRoom, connection, transaction))
            {
                room.Parameters.AddWithValue("@number", Plan.RoomNumber.ToString(CultureInfo.InvariantCulture));
                room.Parameters.AddWithValue("@marker", Plan.Marker); RoomId = B22DatabaseGuard.WriteOne(room);
                FixtureSet.Require(RoomId > 0);
            }
            using (var guest = TestDatabaseGuard.Command(FixtureSet.InsertGuest, connection, transaction))
            {
                guest.Parameters.AddWithValue("@first", "IntegrationB22"); guest.Parameters.AddWithValue("@marker", Plan.Marker);
                guest.Parameters.AddWithValue("@email", Plan.Email); GuestId = B22DatabaseGuard.WriteOne(guest);
                FixtureSet.Require(GuestId > 0);
            }
            using (var booking = TestDatabaseGuard.Command(FixtureSet.InsertBooking, connection, transaction))
            {
                booking.Parameters.AddWithValue("@code", Plan.Code); booking.Parameters.AddWithValue("@guest", GuestId);
                booking.Parameters.AddWithValue("@room", RoomId); booking.Parameters.AddWithValue("@arrival", Plan.Today.Date);
                booking.Parameters.AddWithValue("@departure", Plan.Today.AddDays(1).Date); booking.Parameters.AddWithValue("@marker", Plan.Marker);
                BookingId = B22DatabaseGuard.WriteOne(booking); FixtureSet.Require(BookingId > 0);
            }
            transaction.Commit(); Confirmed = true;
        }
        catch { try { transaction.Rollback(); } catch { } throw; } // Unknown setup Commit never enables cleanup.
    }

    internal static void AssertWorkerOwnership(MySqlConnection connection, string marker, string code, int roomId)
    {
        B22FixturePlan.ValidateWorker(marker, code, roomId);
        using var command = TestDatabaseGuard.Command("SELECT COUNT(*) FROM reservierungen r JOIN zimmer z ON z.id = r.zimmer_id JOIN gaeste g ON g.id = r.gast_id WHERE r.reservierungs_code = @code AND r.zimmer_id = @room AND r.sonderwuensche = @marker AND z.beschreibung = @marker AND g.nachname = @marker AND g.ausweis_nummer = @marker AND r.status = 'Reserviert'", connection);
        command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@room", roomId); command.Parameters.AddWithValue("@marker", marker);
        FixtureSet.Require(Convert.ToInt32(command.ExecuteScalar()) == 1);
    }

    internal (string Booking, string Room, bool Deleted) ReadOwnedRows(MySqlConnection connection, MySqlTransaction? transaction = null)
    {
        var locking = transaction == null ? "" : " FOR UPDATE";
        string roomStatus; bool deleted;
        using (var room = TestDatabaseGuard.Command("SELECT zimmer_nummer, beschreibung, status, geloescht_am FROM zimmer WHERE id = @id" + locking, connection, transaction))
        {
            room.Parameters.AddWithValue("@id", RoomId);
            using var reader = room.ExecuteReader();
            FixtureSet.Require(reader.Read() && reader.GetString(0) == Plan.RoomNumber.ToString(CultureInfo.InvariantCulture) && reader.GetString(1) == Plan.Marker);
            roomStatus = reader.GetString(2); deleted = !reader.IsDBNull(3);
        }
        using var booking = TestDatabaseGuard.Command("SELECT id, reservierungs_code, gast_id, zimmer_id, status, sonderwuensche FROM reservierungen WHERE zimmer_id = @room OR gast_id = @guest ORDER BY id" + locking, connection, transaction);
        booking.Parameters.AddWithValue("@room", RoomId); booking.Parameters.AddWithValue("@guest", GuestId);
        using var rows = booking.ExecuteReader();
        FixtureSet.Require(rows.Read() && rows.GetInt32(0) == BookingId && rows.GetString(1) == Plan.Code && rows.GetInt32(2) == GuestId && rows.GetInt32(3) == RoomId && rows.GetString(5) == Plan.Marker);
        var status = rows.GetString(4);
        FixtureSet.Require(!rows.Read()); // An unexpected booking of this room/guest forbids cleanup.
        return (status, roomStatus, deleted);
    }

    internal void Cleanup(IReadOnlyList<B22WorkerState> workers, bool finalStateVerified)
    {
        B22Safety.RequireCleanup(Confirmed, finalStateVerified, workers); // Before any connection or DELETE.
        B22FixturePlan.ValidateWorker(Plan.Marker, Plan.Code, RoomId);
        FixtureSet.Require(GuestId > 0 && BookingId > 0 && workers.All(worker => worker.ReservationCode == Plan.Code));
        using var connection = B22DatabaseGuard.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var committed = false;
        try
        {
            var state = ReadOwnedRows(connection, transaction); // Room-first lock/ownership/drain probe.
            FixtureSet.Require(B22Safety.StateIsConsistent(workers, state.Booking, state.Room, state.Deleted));
            using (var guest = TestDatabaseGuard.Command("SELECT nachname, email, ausweis_nummer FROM gaeste WHERE id = @id FOR UPDATE", connection, transaction))
            {
                guest.Parameters.AddWithValue("@id", GuestId);
                using var reader = guest.ExecuteReader();
                FixtureSet.Require(reader.Read() && reader.GetString(0) == Plan.Marker && reader.GetString(1) == Plan.Email && reader.GetString(2) == Plan.Marker);
            }
            foreach (var table in new[] { "autovermietungen", "dienstleistungsbestellungen", "restaurant_reservierungen", "taxibestellungen" })
            {
                using var references = TestDatabaseGuard.Command($"SELECT COUNT(*) FROM {table} WHERE reservierung_id = @id", connection, transaction);
                references.Parameters.AddWithValue("@id", BookingId); FixtureSet.Require(Convert.ToInt32(references.ExecuteScalar()) == 0);
            }
            using (var booking = TestDatabaseGuard.Command(FixtureSet.DeleteBooking, connection, transaction))
            {
                booking.Parameters.AddWithValue("@id", BookingId); booking.Parameters.AddWithValue("@code", Plan.Code);
                booking.Parameters.AddWithValue("@room", RoomId); booking.Parameters.AddWithValue("@guest", GuestId);
                booking.Parameters.AddWithValue("@marker", Plan.Marker); B22DatabaseGuard.WriteOne(booking);
            }
            using (var room = TestDatabaseGuard.Command(FixtureSet.DeleteRoom, connection, transaction))
            {
                room.Parameters.AddWithValue("@id", RoomId); room.Parameters.AddWithValue("@number", Plan.RoomNumber.ToString(CultureInfo.InvariantCulture));
                room.Parameters.AddWithValue("@marker", Plan.Marker); B22DatabaseGuard.WriteOne(room);
            }
            using (var guest = TestDatabaseGuard.Command(FixtureSet.DeleteGuest, connection, transaction))
            {
                guest.Parameters.AddWithValue("@id", GuestId); guest.Parameters.AddWithValue("@marker", Plan.Marker);
                guest.Parameters.AddWithValue("@email", Plan.Email); B22DatabaseGuard.WriteOne(guest);
            }
            transaction.Commit(); committed = true;
            Console.WriteLine("B22 CLEANUP confirmed: only three owned fixture rows deleted.");
        }
        catch { if (!committed) try { transaction.Rollback(); } catch { } throw; }
    }

    internal void ReportRetained() => Console.WriteLine($"B22 RETAIN/INSPECT marker={Plan.Marker}; room={RoomId}; guest={GuestId}; booking={BookingId}; code={Plan.Code}; setupConfirmed={Confirmed}. No cleanup retry; inspect actual state.");
}
