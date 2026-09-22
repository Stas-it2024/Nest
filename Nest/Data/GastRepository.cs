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

        public Guest GetByEmail(string email)
        {
            // Vor einer Neuanlage wird nach vorhandenen Kontaktdaten gesucht, damit keine unnötigen Gast-Dubletten entstehen.
            if (string.IsNullOrEmpty(email)) return null;
            using var conn = DB.GetConnection();
            conn.Open();
            const string sql = "SELECT * FROM gaeste WHERE email = @Email";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Email", email);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapGuest(reader);
            return null;
        }
        public Guest GetByPhone(string telefon)
        {
            if (string.IsNullOrEmpty(telefon)) return null;
            using var conn = DB.GetConnection();
            conn.Open();
            const string sql = "SELECT * FROM gaeste WHERE telefon = @Phone";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Phone", telefon);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return MapGuest(reader);
            return null;
        }

        public void Add(Guest guest)
        {
            // using var schließt Datenbankressourcen automatisch, auch wenn beim Speichern eine Ausnahme auftritt.
            using var conn = DB.GetConnection();
            conn.Open();
            const string sql = @"
                INSERT INTO gaeste
                    (vorname, nachname, email, telefon, ausweis_typ, ausweis_nummer, nationalitaet, hinzugefuegt_am)
                VALUES
                    (@FirstName, @LastName, @Email, @Phone, @IdType, @IdNumber, @Nationality, @DateAdded);
                SELECT LAST_INSERT_ID();";
            using var cmd = new MySqlCommand(sql, conn);
            // Die Werte bleiben durch Parameter vom SQL-Text getrennt; das reduziert das Risiko von SQL-Injection.
            cmd.Parameters.AddWithValue("@FirstName", guest.FirstName);
            cmd.Parameters.AddWithValue("@LastName", guest.LastName);
            // Optionale C#-Nullwerte werden als echtes SQL-NULL gespeichert.
            cmd.Parameters.AddWithValue("@Email", (object)guest.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", (object)guest.Phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IdType", (object)guest.IdType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IdNumber", (object)guest.IdNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Nationality", (object)guest.Nationality ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DateAdded", guest.DateAdded ?? DateTime.Now);
            // Die erzeugte Datenbank-ID wird für die anschließende Zimmerreservierung benötigt.
            var newId = Convert.ToInt32(cmd.ExecuteScalar());
            guest.Id = newId;
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
