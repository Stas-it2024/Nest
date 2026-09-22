using MySql.Data.MySqlClient;
using Nest.Models;
using System;

namespace Nest.Data
{
    public class TaxiOrderRepository
    {
        public void Add(TaxiOrder order)
        {
            // Da nur ein INSERT ausgeführt wird, ist keine zusätzliche Transaktion erforderlich.
            // using var sorgt auch im Fehlerfall für das automatische Freigeben der Ressourcen.
            using var connection = DB.GetConnection();
            connection.Open();

            const string sql = @"
                INSERT INTO taxibestellungen
                (
                    reservierung_id,
                    gast_name,
                    gast_telefon,
                    abholort,
                    abhol_datum,
                    abhol_zeit,
                    fahrzeug_typ,
                    status
                )
                VALUES
                (
                    @ReservationId,
                    @GuestName,
                    @GuestPhone,
                    @PickupLocation,
                    @PickupDate,
                    @PickupTime,
                    @VehicleType,
                    @Status
                );";

            // Parameter trennen Benutzereingaben vom SQL-Text und reduzieren so das SQL-Injection-Risiko.
            using var command = new MySqlCommand(sql, connection);
            // Eine fehlende Zimmerreservierungs-ID und eine leere optionale Telefonnummer werden als SQL-NULL gespeichert.
            command.Parameters.Add("@ReservationId", MySqlDbType.Int32).Value =
                order.ReservationId.HasValue ? order.ReservationId.Value : DBNull.Value;
            command.Parameters.Add("@GuestName", MySqlDbType.VarChar).Value = order.GuestName;
            command.Parameters.Add("@GuestPhone", MySqlDbType.VarChar).Value =
                string.IsNullOrWhiteSpace(order.GuestPhone) ? DBNull.Value : order.GuestPhone;
            command.Parameters.Add("@PickupLocation", MySqlDbType.VarChar).Value = order.PickupLocation;
            command.Parameters.Add("@PickupDate", MySqlDbType.Date).Value = order.PickupDate.Date;
            command.Parameters.Add("@PickupTime", MySqlDbType.Time).Value = order.PickupTime;
            command.Parameters.Add("@VehicleType", MySqlDbType.VarChar).Value = order.VehicleType;
            command.Parameters.Add("@Status", MySqlDbType.VarChar).Value = order.Status;

            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "Die Taxianfrage konnte nicht vollständig gespeichert werden.");
            }

            // Die erzeugte ID bestätigt, welcher Datensatz gespeichert wurde.
            order.Id = Convert.ToInt32(command.LastInsertedId);
        }
    }
}
