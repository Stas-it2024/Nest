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
        // Eine zentrale Verbindungszeichenfolge verhindert unterschiedliche Datenbankeinstellungen in den Repositories.
        private const string ConnectionString =
            "server=localhost;port=3306;database=hotel;uid=root;pwd=root;SslMode=Disabled;";

        public static MySqlConnection GetConnection()
        {
            // Jeder Aufruf liefert eine neue Verbindung, deren Lebensdauer das aufrufende Repository kontrolliert.
            return new MySqlConnection(ConnectionString);
        }
    }
}
