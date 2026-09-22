using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using MySql.Data.MySqlClient;
using Nest.Models;

namespace Nest.Data
{
    public class UserRepository
    {
        public User? Login(string benutzername, string passwort)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                SELECT *
                FROM benutzer
                WHERE benutzername = @benutzername
                    AND passwort = @passwort
                    AND status = 'Aktiv'";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@benutzername", benutzername);
            cmd.Parameters.AddWithValue("@passwort", passwort);

            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return new User
                {
                    Id = reader.GetInt32("id"),
                    UserName = reader.GetString("benutzername"),
                    FullName = reader.GetString("vollstaendiger_name"),
                    Role = reader.GetString("rolle"),
                    Status = reader.GetString("status")
                };
            }

            return null;
        }

        // Daten für Tabelle
        public List<User> GetAll()
        {
            var benutzer = new List<User>();

            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                        SELECT id,
                               benutzername,
                               vollstaendiger_name,
                               rolle,
                               status,
                               hinzugefuegt_am,
                               geloescht_am
                        FROM benutzer
                        WHERE geloescht_am IS NULL
                        ORDER BY id";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                benutzer.Add(new User
                {
                    Id = reader.GetInt32("id"),
                    UserName = reader.GetString("benutzername"),
                    FullName = reader.GetString("vollstaendiger_name"),
                    Role = reader.GetString("rolle"),
                    Status = reader.GetString("status"),
                    DateAdded = reader.GetDateTime("hinzugefuegt_am"),
                    DateDelete = reader.IsDBNull(reader.GetOrdinal("geloescht_am"))
                        ? null
                        : reader.GetDateTime("geloescht_am")
                });
            }

            return benutzer;
        }

        // Add a new user to the database
        public bool Add(User user)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                        INSERT INTO benutzer
                        (benutzername, passwort, vollstaendiger_name, rolle, status, hinzugefuegt_am)
                        VALUES
                        (@benutzername, @passwort, @vollstaendiger_name, @rolle, @status, @hinzugefuegt_am)";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@benutzername", user.UserName);
            cmd.Parameters.AddWithValue("@passwort", user.Password);
            cmd.Parameters.AddWithValue("@vollstaendiger_name", user.FullName);
            cmd.Parameters.AddWithValue("@rolle", user.Role);
            cmd.Parameters.AddWithValue("@status", user.Status);
            cmd.Parameters.AddWithValue("@hinzugefuegt_am", user.DateAdded);

            try
            {
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                return false;
            }
        }

        // Update an existing user in the database
        public void Update(User user)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql;

            if (string.IsNullOrWhiteSpace(user.Password))
            {
                sql = @"
            UPDATE benutzer
            SET benutzername = @benutzername,
                vollstaendiger_name = @vollstaendiger_name,
                rolle = @rolle,
                status = @status
            WHERE id = @id
              AND geloescht_am IS NULL";
            }
            else
            {
                sql = @"
            UPDATE benutzer
            SET benutzername = @benutzername,
                vollstaendiger_name = @vollstaendiger_name,
                passwort = @passwort,
                rolle = @rolle,
                status = @status
            WHERE id = @id
              AND geloescht_am IS NULL";
            }

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", user.Id);
            cmd.Parameters.AddWithValue("@benutzername", user.UserName);
            cmd.Parameters.AddWithValue("@vollstaendiger_name", user.FullName);
            cmd.Parameters.AddWithValue("@rolle", user.Role);
            cmd.Parameters.AddWithValue("@status", user.Status);

            if (!string.IsNullOrWhiteSpace(user.Password))
            {
                cmd.Parameters.AddWithValue("@passwort", user.Password);
            }

            cmd.ExecuteNonQuery();
        }

        // Soft delete a user by setting the geloescht_am field
        public void Delete(int userId)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                        UPDATE benutzer
                        SET geloescht_am = @geloescht_am
                        WHERE id = @id
                          AND geloescht_am IS NULL";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", userId);
            cmd.Parameters.AddWithValue("@geloescht_am", DateTime.Now);

            cmd.ExecuteNonQuery();
        }
    }
}
