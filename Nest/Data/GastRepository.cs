using MySql.Data.MySqlClient;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Data
{
    public class GastRepository
    {
        public List<Guest> GetAll()
        {
            var gaeste = new List<Guest>();
            using var conn = DB.GetConnection();
            conn.Open();
            const string sql = @"
                           SELECT
                                 id,
                                 vorname,
                                 nachname,
                                 email,
                                 telefon,
                                 ausweis_typ,
                                 ausweis_nummer,
                                 nationalitaet,
                                 hinzugefuegt_am
                          FROM gaeste;";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                gaeste.Add(MapGuest(reader));
            }
            return gaeste;
        }

        public Guest? GetByEmail(string? email)
        {
            // Vor einer Neuanlage wird nach vorhandenen Kontaktdaten gesucht, damit keine unnötigen Gast-Dubletten entstehen.
            if (string.IsNullOrWhiteSpace(email)) return null;
            using var conn = DB.GetConnection();
            conn.Open();
            const string sql = "SELECT * FROM gaeste WHERE email = @Email";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Email", email.Trim());
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapGuest(reader);
            return null;
        }
        public Guest? GetByPhone(string? telefon)
        {
            if (string.IsNullOrWhiteSpace(telefon)) return null;
            using var conn = DB.GetConnection();
            conn.Open();
            const string sql = "SELECT * FROM gaeste WHERE telefon = @Phone";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Phone", telefon.Trim());
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapGuest(reader);
            return null;
        }

        internal int FindOrAdd(Guest guest, MySqlConnection conn, MySqlTransaction transaction)
        {
            var email = string.IsNullOrWhiteSpace(guest.Email) ? null : guest.Email.Trim();
            var telefon = string.IsNullOrWhiteSpace(guest.Phone) ? null : guest.Phone.Trim();
            // Gemeinsame Familienkontakte reichen nicht aus: auch Vor- und Nachname müssen passen.
            if (email != null || telefon != null)
            {
                const string sql = @"
                    SELECT id FROM gaeste
                    WHERE vorname = @FirstName AND nachname = @LastName
                      AND ((@Email IS NOT NULL AND email = @Email)
                        OR (@Phone IS NOT NULL AND telefon = @Phone))
                    ORDER BY id LIMIT 1";
                using var cmd = new MySqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@FirstName", guest.FirstName.Trim());
                cmd.Parameters.AddWithValue("@LastName", guest.LastName.Trim());
                cmd.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Phone", (object?)telefon ?? DBNull.Value);
                var existingId = cmd.ExecuteScalar();
                if (existingId != null && existingId != DBNull.Value)
                    return Convert.ToInt32(existingId);
            }

            return Insert(guest, conn, transaction);
        }

        public void Add(Guest guest)
        {
            // using var schließt Datenbankressourcen automatisch, auch wenn beim Speichern eine Ausnahme auftritt.
            using var conn = DB.GetConnection();
            conn.Open();
            guest.Id = Insert(guest, conn, null);
        }

        private static int Insert(Guest guest, MySqlConnection conn, MySqlTransaction? transaction)
        {
            const string sql = @"
                INSERT INTO gaeste
                    (vorname, nachname, email, telefon, ausweis_typ, ausweis_nummer, nationalitaet, hinzugefuegt_am)
                VALUES
                    (@FirstName, @LastName, @Email, @Phone, @IdType, @IdNumber, @Nationality, @DateAdded);
                SELECT LAST_INSERT_ID();";
            using var cmd = new MySqlCommand(sql, conn, transaction);
            // Die Werte bleiben durch Parameter vom SQL-Text getrennt; das reduziert das Risiko von SQL-Injection.
            cmd.Parameters.AddWithValue("@FirstName", guest.FirstName.Trim());
            cmd.Parameters.AddWithValue("@LastName", guest.LastName.Trim());
            // Optionale C#-Nullwerte werden als echtes SQL-NULL gespeichert.
            cmd.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(guest.Email) ? DBNull.Value : guest.Email.Trim());
            cmd.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(guest.Phone) ? DBNull.Value : guest.Phone.Trim());
            cmd.Parameters.AddWithValue("@IdType", (object)guest.IdType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IdNumber", (object)guest.IdNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Nationality", (object)guest.Nationality ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateAdded", guest.DateAdded ?? DateTime.Now);
            // Die erzeugte Datenbank-ID wird für die anschließende Zimmerreservierung benötigt.
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private Guest MapGuest(MySqlDataReader reader)
        {
            return new Guest
            {
                Id = reader.GetInt32("id"),
                FirstName = reader.GetString("vorname"),
                LastName = reader.GetString("nachname"),
                Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
                Phone = reader.IsDBNull(reader.GetOrdinal("telefon")) ? null : reader.GetString("telefon"),
                IdType = reader.IsDBNull(reader.GetOrdinal("ausweis_typ")) ? null : reader.GetString("ausweis_typ"),
                IdNumber = reader.IsDBNull(reader.GetOrdinal("ausweis_nummer")) ? null : reader.GetString("ausweis_nummer"),
                Nationality = reader.IsDBNull(reader.GetOrdinal("nationalitaet")) ? null : reader.GetString("nationalitaet"),
                DateAdded = reader.IsDBNull(reader.GetOrdinal("hinzugefuegt_am")) ? (DateTime?)null : reader.GetDateTime("hinzugefuegt_am")
            };
        }
    }
}
