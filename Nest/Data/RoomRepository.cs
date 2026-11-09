using MySql.Data.MySqlClient;
using Nest.Models;
using System.Data;

namespace Nest.Data
{
    public enum RoomWriteResult
    {
        Success, Missing, Occupied, FutureReservation, InvalidStatus,
        NumberConflict, TechnicalFailure, UncertainOutcome
    }

    public class RoomRepository
    {
        public List<Room> GetAll()
        {
            var zimmer = new List<Room>();

            using var conn = DB.GetConnection();
            DB.OpenConnection(conn);

            const string sql = @"
                SELECT id, zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status,
                       beschreibung, bild_pfad, hinzugefuegt_am, geloescht_am
                FROM zimmer
                WHERE geloescht_am IS NULL
                ORDER BY zimmer_nummer;";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                zimmer.Add(MapRoom(reader));
            }

            return zimmer;
        }

        public List<Room> GetAvailableRooms()
        {
            var zimmer = new List<Room>();

            using var conn = DB.GetConnection();
            DB.OpenConnection(conn);

            const string sql = @"
                SELECT id, zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status,
                       beschreibung, bild_pfad, hinzugefuegt_am, geloescht_am
                FROM zimmer
                WHERE status = 'Verfuegbar'
                  AND geloescht_am IS NULL
                ORDER BY zimmer_nummer;";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                zimmer.Add(MapRoom(reader));
            }

            return zimmer;
        }

        public List<Room> GetAvailableRooms(DateTime requestedCheckIn, DateTime requestedCheckOut)
        {
            // Ungültige Zeiträume werden vor dem Datenbankzugriff abgefangen.
            if (requestedCheckOut.Date <= requestedCheckIn.Date)
            {
                return new List<Room>();
            }

            var zimmer = new List<Room>();

            // using var gibt Verbindung, Befehl und Reader auch bei einem Fehler automatisch frei.
            using var conn = DB.GetConnection();
            DB.OpenConnection(conn);

            // NOT EXISTS liefert nur Zimmer, deren bestehende Buchungen den gewünschten Zeitraum nicht überschneiden.
            const string sql = @"
                SELECT rm.id, rm.zimmer_nummer, rm.zimmer_typ, rm.kapazitaet, rm.preis_pro_nacht,
                       rm.status, rm.beschreibung, rm.bild_pfad, rm.hinzugefuegt_am, rm.geloescht_am
                FROM zimmer rm
                WHERE rm.status IN ('Verfuegbar', 'Reserviert', 'Belegt')
                  AND rm.geloescht_am IS NULL
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM reservierungen r
                      WHERE r.zimmer_id = rm.id
                        AND r.status IN ('Reserviert', 'Eingecheckt')
                        AND @RequestedCheckIn < r.abreise_datum
                        AND @RequestedCheckOut > r.anreise_datum
                  )
                ORDER BY rm.zimmer_nummer;";

            using var cmd = new MySqlCommand(sql, conn);
            // Parameter schützen vor SQL-Injection und übergeben die Datumswerte typgerecht an MySQL.
            cmd.Parameters.Add("@RequestedCheckIn", MySqlDbType.Date).Value = requestedCheckIn.Date;
            cmd.Parameters.Add("@RequestedCheckOut", MySqlDbType.Date).Value = requestedCheckOut.Date;
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                zimmer.Add(MapRoom(reader));
            }

            return zimmer;
        }

        public Room? GetById(int id)
        {
            using var conn = DB.GetConnection();
            DB.OpenConnection(conn);

            const string sql = @"
                SELECT id, zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status,
                       beschreibung, bild_pfad, hinzugefuegt_am, geloescht_am
                FROM zimmer
                WHERE id = @Id
                  AND geloescht_am IS NULL;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();

            return reader.Read() ? MapRoom(reader) : null;
        }

        public RoomWriteResult Add(Room room)
        {
            if (room.Status != "Verfuegbar" && room.Status != "Wartung" && room.Status != "Reserviert")
                return RoomWriteResult.InvalidStatus;

            int insertedId = 0;
            var result = ExecuteWrite((conn, transaction) =>
            {

                const string sql = @"
                    INSERT INTO zimmer
                        (zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status, beschreibung, bild_pfad, hinzugefuegt_am)
                    VALUES
                        (@RoomNumber, @Type, @Capacity, @PricePerNight, @Status, @Description, @ImagePath, @DateAdded);
                    SELECT LAST_INSERT_ID();";

                using var cmd = new MySqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@RoomNumber", room.RoomNumber);
                cmd.Parameters.AddWithValue("@Type", room.Type);
                cmd.Parameters.AddWithValue("@Capacity", room.Capacity);
                cmd.Parameters.AddWithValue("@PricePerNight", room.PricePerNight);
                cmd.Parameters.AddWithValue("@Status", room.Status);
                cmd.Parameters.AddWithValue("@Description", (object?)room.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ImagePath", (object?)room.ImagePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue(
                    "@DateAdded",
                    room.DateAdded == default ? DateTime.Now : room.DateAdded);

                insertedId = Convert.ToInt32(cmd.ExecuteScalar());
                return insertedId > 0 ? RoomWriteResult.Success : RoomWriteResult.TechnicalFailure;
            });
            if (result == RoomWriteResult.Success)
                room.Id = insertedId;
            return result;
        }

        public RoomWriteResult Update(Room room)
        {
            return ExecuteWrite((conn, transaction) =>
            {
                if (!LockActiveRoom(conn, transaction, room.Id))
                    return RoomWriteResult.Missing;
                var occupied = ReadReservationProtection(conn, transaction, room.Id, includeFuture: false);
                if (occupied == RoomWriteResult.Occupied && room.Status != "Belegt")
                    return RoomWriteResult.Occupied;
                if ((room.Status == "Belegt" && occupied != RoomWriteResult.Occupied)
                    || (room.Status != "Verfuegbar" && room.Status != "Reserviert"
                        && room.Status != "Wartung" && room.Status != "Belegt"))
                    return RoomWriteResult.InvalidStatus;

                const string sql = @"
                    UPDATE zimmer
                    SET zimmer_nummer = @RoomNumber,
                        zimmer_typ = @Type,
                        kapazitaet = @Capacity,
                        preis_pro_nacht = @PricePerNight,
                        status = @Status,
                        beschreibung = @Description,
                        bild_pfad = COALESCE(@ImagePath, bild_pfad)
                    WHERE id = @Id
                      AND geloescht_am IS NULL;";

                using var cmd = new MySqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@RoomNumber", room.RoomNumber);
                cmd.Parameters.AddWithValue("@Type", room.Type);
                cmd.Parameters.AddWithValue("@Capacity", room.Capacity);
                cmd.Parameters.AddWithValue("@PricePerNight", room.PricePerNight);
                cmd.Parameters.AddWithValue("@Status", room.Status);
                cmd.Parameters.AddWithValue("@Description", (object?)room.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ImagePath", (object?)room.ImagePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", room.Id);
                return cmd.ExecuteNonQuery() == 1 ? RoomWriteResult.Success : RoomWriteResult.Missing;
            });
        }

        public RoomWriteResult Delete(int id)
        {
            return ExecuteWrite((conn, transaction) =>
            {
                if (!LockActiveRoom(conn, transaction, id))
                    return RoomWriteResult.Missing;
                var protection = ReadReservationProtection(conn, transaction, id, includeFuture: true);
                if (protection != RoomWriteResult.Success)
                    return protection;

                const string sql = @"
                    UPDATE zimmer
                    SET geloescht_am = @DateDelete
                    WHERE id = @Id
                      AND geloescht_am IS NULL;";

                using var cmd = new MySqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@DateDelete", DateTime.Now);
                cmd.Parameters.AddWithValue("@Id", id);
                return cmd.ExecuteNonQuery() == 1 ? RoomWriteResult.Success : RoomWriteResult.Missing;
            });
        }

        private static bool LockActiveRoom(MySqlConnection conn, MySqlTransaction transaction, int id)
        {
            const string sql = @"
                SELECT id, geloescht_am FROM zimmer WHERE id = @Id FOR UPDATE;";
            using var cmd = new MySqlCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@Id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() && reader.GetInt32("id") == id
                && reader.IsDBNull(reader.GetOrdinal("geloescht_am"));
        }

        private static RoomWriteResult ReadReservationProtection(MySqlConnection conn,
            MySqlTransaction transaction, int id, bool includeFuture)
        {
            // Alle Prüfungen folgen der Zimmersperre. Das Abreisedatum ist einschließlich geschützt.
            var sql = includeFuture
                ? @"SELECT id, status FROM reservierungen
                    WHERE zimmer_id = @Id AND (status = 'Eingecheckt'
                      OR (status = 'Reserviert' AND abreise_datum >= @Today))
                    ORDER BY id FOR UPDATE;"
                : @"SELECT id, status FROM reservierungen
                    WHERE zimmer_id = @Id AND status = 'Eingecheckt'
                    ORDER BY id FOR UPDATE;";
            using var cmd = new MySqlCommand(sql, conn, transaction);
            cmd.Parameters.AddWithValue("@Id", id);
            if (includeFuture)
                cmd.Parameters.Add("@Today", MySqlDbType.Date).Value = DateTime.Today;
            using var reader = cmd.ExecuteReader();
            var result = RoomWriteResult.Success;
            while (reader.Read())
            {
                if (reader.GetString("status") == "Eingecheckt")
                    return RoomWriteResult.Occupied;
                result = RoomWriteResult.FutureReservation;
            }
            return result;
        }

        private static RoomWriteResult ExecuteWrite(Func<MySqlConnection, MySqlTransaction, RoomWriteResult> write)
        {
            bool committed = false, commitAttempted = false, rollbackFailed = false;
            RoomWriteResult result;
            try
            {
                using var conn = DB.GetConnection();
                DB.OpenConnection(conn);
                using var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                try
                {
                    result = write(conn, transaction);
                    if (result == RoomWriteResult.Success)
                    {
                        commitAttempted = true;
                        transaction.Commit();
                        committed = true;
                    }
                }
                finally
                {
                    if (!committed)
                    {
                        try { transaction.Rollback(); }
                        catch (Exception) { rollbackFailed = true; }
                    }
                }
            }
            catch (Exception ex)
            {
                if (committed)
                    return RoomWriteResult.Success;
                if (commitAttempted || rollbackFailed)
                    return RoomWriteResult.UncertainOutcome;
                return ex is MySqlException mysql && mysql.Number == 1062
                    ? RoomWriteResult.NumberConflict : RoomWriteResult.TechnicalFailure;
            }
            return rollbackFailed ? RoomWriteResult.UncertainOutcome : result;
        }

        private static Room MapRoom(MySqlDataReader reader)
        {
            // Die Zuordnung an einer Stelle hält die Datenbankspalten und das Model konsistent.
            return new Room
            {
                Id = reader.GetInt32("id"),
                RoomNumber = reader.GetInt32("zimmer_nummer"),
                Type = reader.GetString("zimmer_typ"),
                Capacity = reader.GetInt32("kapazitaet"),
                PricePerNight = reader.GetDecimal("preis_pro_nacht"),
                Status = reader.GetString("status"),
                Description = reader.IsDBNull(reader.GetOrdinal("beschreibung"))
                    ? null
                    : reader.GetString("beschreibung"),
                ImagePath = reader.IsDBNull(reader.GetOrdinal("bild_pfad"))
                    ? null
                    : reader.GetString("bild_pfad"),
                DateAdded = reader.GetDateTime("hinzugefuegt_am"),
                DateDelete = reader.IsDBNull(reader.GetOrdinal("geloescht_am"))
                    ? null
                    : reader.GetDateTime("geloescht_am")
            };
        }
    }
}
