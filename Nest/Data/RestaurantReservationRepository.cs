using MySql.Data.MySqlClient;
using Nest.Models;
using System;

namespace Nest.Data
{
    public class RestaurantReservationRepository
    {
        public void Add(RestaurantReservation reservation)
        {
            // Für diesen einzelnen INSERT ist keine zusätzliche Transaktion nötig; der Befehl ist selbst atomar.
            // using var schließt die Datenbankressourcen zuverlässig.
            using var connection = DB.GetConnection();
            connection.Open();

            const string sql = @"
                INSERT INTO restaurant_reservierungen
                (
                    reservierung_id,
                    gast_name,
                    reservierungs_datum,
                    reservierungs_zeit,
                    gast_anzahl,
                    anlass,
                    sonderwuensche,
                    status
                )
                VALUES
                (
                    @ReservationId,
                    @GuestName,
                    @ReservationDate,
                    @ReservationTime,
                    @GuestCount,
                    @Occasion,
                    @SpecialRequests,
                    @Status
                );";

            // Parametrisierte Abfragen halten Eingaben aus dem SQL-Text heraus und schützen vor SQL-Injection.
            using var command = new MySqlCommand(sql, connection);
            // Ohne Zimmerbezug bleibt ReservationId leer und wird als SQL-NULL gespeichert.
            command.Parameters.Add("@ReservationId", MySqlDbType.Int32).Value =
                reservation.ReservationId.HasValue ? reservation.ReservationId.Value : DBNull.Value;
            command.Parameters.Add("@GuestName", MySqlDbType.VarChar).Value = reservation.GuestName;
            command.Parameters.Add("@ReservationDate", MySqlDbType.Date).Value = reservation.ReservationDate.Date;
            command.Parameters.Add("@ReservationTime", MySqlDbType.Time).Value = reservation.ReservationTime;
            command.Parameters.Add("@GuestCount", MySqlDbType.Int32).Value = reservation.GuestCount;
            command.Parameters.Add("@Occasion", MySqlDbType.VarChar).Value = reservation.Occasion;
            // Leere optionale Wünsche werden als SQL-NULL statt als bedeutungsloser Leertext gespeichert.
            command.Parameters.Add("@SpecialRequests", MySqlDbType.Text).Value =
                string.IsNullOrWhiteSpace(reservation.SpecialRequests)
                    ? DBNull.Value
                    : reservation.SpecialRequests;
            command.Parameters.Add("@Status", MySqlDbType.VarChar).Value = reservation.Status;

            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "Die Tischreservierung konnte nicht vollständig gespeichert werden.");
            }

            // Die von MySQL erzeugte ID wird für eine spätere Zuordnung in das Model übernommen.
            reservation.Id = Convert.ToInt32(command.LastInsertedId);
        }
    }
}
