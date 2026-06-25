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
        public User? Login(string username, string password)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                SELECT *
                FROM users
                WHERE username = @username
                    AND password = @password
                    AND status = 'Active'";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@password", password);

            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                return new User
                {
                    Id = reader.GetInt32("id"),
                    UserName = reader.GetString("username"),
                    FullName = reader.GetString("full_name"),
                    Role = reader.GetString("role"),
                    Status = reader.GetString("status")
                };
            }

            return null;
        }
    }
}
