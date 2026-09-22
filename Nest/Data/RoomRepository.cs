using MySql.Data.MySqlClient;
using Nest.Models;

namespace Nest.Data
{
    public class RoomRepository
    {
        public List<Room> GetAll()
        {
            var zimmer = new List<Room>();

            using var conn = DB.GetConnection();
            conn.Open();

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
            conn.Open();

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
            conn.Open();

            // NOT EXISTS liefert nur Zimmer, deren bestehende Buchungen den gewünschten Zeitraum nicht überschneiden.
            const string sql = @"
                SELECT rm.id, rm.zimmer_nummer, rm.zimmer_typ, rm.kapazitaet, rm.preis_pro_nacht,
                       rm.status, rm.beschreibung, rm.bild_pfad, rm.hinzugefuegt_am, rm.geloescht_am
                FROM zimmer rm
                WHERE rm.status = 'Verfuegbar'
                  AND rm.geloescht_am IS NULL
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM reservierungen r
                      WHERE r.zimmer_id = rm.id
                        AND r.status <> 'Storniert'
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
            conn.Open();

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

        public void Add(Room room)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            const string sql = @"
                INSERT INTO zimmer
                    (zimmer_nummer, zimmer_typ, kapazitaet, preis_pro_nacht, status, beschreibung, bild_pfad, hinzugefuegt_am)
                VALUES
                    (@RoomNumber, @Type, @Capacity, @PricePerNight, @Status, @Description, @ImagePath, @DateAdded);
                SELECT LAST_INSERT_ID();";

            using var cmd = new MySqlCommand(sql, conn);
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

            room.Id = Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void Update(Room room)
        {
            using var conn = DB.GetConnection();
            conn.Open();

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

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@RoomNumber", room.RoomNumber);
            cmd.Parameters.AddWithValue("@Type", room.Type);
            cmd.Parameters.AddWithValue("@Capacity", room.Capacity);
            cmd.Parameters.AddWithValue("@PricePerNight", room.PricePerNight);
            cmd.Parameters.AddWithValue("@Status", room.Status);
            cmd.Parameters.AddWithValue("@Description", (object?)room.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ImagePath", (object?)room.ImagePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Id", room.Id);
            cmd.ExecuteNonQuery();
        }

        public bool Delete(int id)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            const string sql = @"
                UPDATE zimmer
                SET geloescht_am = @DateDelete
                WHERE id = @Id
                  AND geloescht_am IS NULL;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@DateDelete", DateTime.Now);
            cmd.Parameters.AddWithValue("@Id", id);
            return cmd.ExecuteNonQuery() == 1;
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
