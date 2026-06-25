using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Data
{
    public static class DB
    {
        private const string ConnectionString =
            "server=localhost;port=3306;database=hotel;uid=root;pwd=root;SslMode=Disabled;";

        public static MySqlConnection GetConnection()
        {
            return new MySqlConnection(ConnectionString);
        }
    }
}
