using MySql.Data.MySqlClient;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Data
{
    public class ServiceRepository
    {
        // Daten lesen von DB
        public List<Service> GetAll()
        {
            var dienstleistungen = new List<Service>();

            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                SELECT id,
                       dienstleistungs_name,
                       kategorie,
                       preis,
                       status,
                       hinzugefuegt_am,
                       geloescht_am
                FROM dienstleistungen
                WHERE geloescht_am IS NULL
                ORDER BY id";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                dienstleistungen.Add(new Service
                {
                    Id = reader.GetInt32("id"),
                    ServiceName = reader.GetString("dienstleistungs_name"),
                    Category = reader.GetString("kategorie"),
                    Price = reader.GetDecimal("preis"),
                    Status = reader.GetString("status"),
                    DateAdded = reader.GetDateTime("hinzugefuegt_am"),
                    DateDelete = reader.IsDBNull(reader.GetOrdinal("geloescht_am"))
                        ? null
                        : reader.GetDateTime("geloescht_am")
                });
            }

            return dienstleistungen;
        }


        // Hinzufügen
        public bool Add(Service service)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                INSERT INTO dienstleistungen
                (dienstleistungs_name, kategorie, preis, status)
                VALUES
                (@dienstleistungs_name, @kategorie, @preis, @status)";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@dienstleistungs_name", service.ServiceName);
            cmd.Parameters.AddWithValue("@kategorie", service.Category);
            cmd.Parameters.AddWithValue("@preis", service.Price);
            cmd.Parameters.AddWithValue("@status", service.Status);

            try
            {
                cmd.ExecuteNonQuery();

                // ID des neu erstellten Datensatzes zurückgeben
                service.Id = (int)cmd.LastInsertedId;

                return true;
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return false;
            }
        }


        // Aktualisieren
        public void Update(Service service)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                UPDATE dienstleistungen
                SET dienstleistungs_name = @dienstleistungs_name,
                    kategorie = @kategorie,
                    preis = @preis,
                    status = @status
                WHERE id = @id
                  AND geloescht_am IS NULL";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", service.Id);
            cmd.Parameters.AddWithValue("@dienstleistungs_name", service.ServiceName);
            cmd.Parameters.AddWithValue("@kategorie", service.Category);
            cmd.Parameters.AddWithValue("@preis", service.Price);
            cmd.Parameters.AddWithValue("@status", service.Status);

            cmd.ExecuteNonQuery();
        }


        // Löschen - Soft Delete
        public void Delete(int serviceId)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                UPDATE dienstleistungen
                SET geloescht_am = @geloescht_am
                WHERE id = @id
                  AND geloescht_am IS NULL";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", serviceId);
            cmd.Parameters.AddWithValue("@geloescht_am", DateTime.Now);

            cmd.ExecuteNonQuery();
        }
    }
}
