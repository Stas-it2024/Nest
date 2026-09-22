using MySql.Data.MySqlClient;
using Nest.Models;
using System;

namespace Nest.Data
{
    public class AutoRentalRepository
    {
        public void Add(AutoRental rental)
        {
            // Ein einzelner INSERT ist bereits atomar; deshalb ist hier keine zusätzliche Transaktion nötig.
            // using var gibt Verbindung und Befehl auch bei einem Fehler automatisch frei.
            using var connection = DB.GetConnection();
            connection.Open();

            const string sql = @"
                INSERT INTO autovermietungen
                (
                    reservierung_id,
                    fahrer_name,
                    telefon,
                    fahrzeug_kategorie,
                    abhol_datum,
                    rueckgabe_datum,
                    tagespreis,
                    gesamtpreis,
                    status
                )
                VALUES
                (
                    @ReservationId,
                    @DriverName,
                    @Phone,
                    @VehicleCategory,
                    @PickupDate,
                    @ReturnDate,
                    @DailyPrice,
                    @TotalPrice,
                    @Status
                );";

            // Parametrisierte Werte schützen vor SQL-Injection und bewahren die MySQL-Datentypen.
            using var command = new MySqlCommand(sql, connection);
            // null bedeutet: Die Miete wurde unabhängig von einer Zimmerreservierung angefragt.
            command.Parameters.Add("@ReservationId", MySqlDbType.Int32).Value =
                rental.ReservationId.HasValue ? rental.ReservationId.Value : DBNull.Value;
            command.Parameters.Add("@DriverName", MySqlDbType.VarChar).Value = rental.DriverName;
            command.Parameters.Add("@Phone", MySqlDbType.VarChar).Value = rental.Phone;
            command.Parameters.Add("@VehicleCategory", MySqlDbType.VarChar).Value = rental.VehicleCategory;
            command.Parameters.Add("@PickupDate", MySqlDbType.Date).Value = rental.PickupDate.Date;
            command.Parameters.Add("@ReturnDate", MySqlDbType.Date).Value = rental.ReturnDate.Date;
            command.Parameters.Add("@DailyPrice", MySqlDbType.Decimal).Value = rental.DailyPrice;
            command.Parameters.Add("@TotalPrice", MySqlDbType.Decimal).Value = rental.TotalPrice;
            command.Parameters.Add("@Status", MySqlDbType.VarChar).Value = rental.Status;

            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "Die Mietanfrage konnte nicht vollständig gespeichert werden.");
            }

            // Die von MySQL erzeugte ID wird in das Model zurückgeschrieben.
            rental.Id = Convert.ToInt32(command.LastInsertedId);
        }
    }
}
