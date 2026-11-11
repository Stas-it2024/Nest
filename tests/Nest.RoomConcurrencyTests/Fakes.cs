using Nest.Models;
using System.Data;
using System.Globalization;

namespace RoomTests
{
    // Deterministic transport/transaction model, NOT a MariaDB locking simulation.
    internal static class State
    {
        internal static RoomOriginalValues? Row, Pending;
        internal static bool Occupied, Future;
        internal static string Fault = "";
        internal static int Affected, Writes, Commits, Rollbacks, ConnectionsDisposed, TransactionsDisposed;
        internal static readonly List<(string Sql, Dictionary<string, object?> Parameters)> Commands = new();
        internal static readonly List<string> Events = new();
        internal static void Reset(RoomOriginalValues? row)
        {
            Row = row; Pending = null; Occupied = Future = false; Fault = ""; Affected = 1;
            Writes = Commits = Rollbacks = ConnectionsDisposed = TransactionsDisposed = 0;
            Commands.Clear(); Events.Clear();
        }
        internal static void Throw(string fault)
        {
            Events.Add(fault);
            if (Fault.Split(',').Contains(fault)) throw new InvalidOperationException("PRIVATE " + fault);
        }
    }
}
namespace RoomTests.Data
{
    internal static class DB
    {
        public static Fakes.MySqlConnection GetConnection() { State.Throw("create"); return new(); }
        public static void OpenConnection(Fakes.MySqlConnection connection) { State.Throw("open"); }
    }
}
namespace RoomTests.Fakes
{
    internal enum MySqlDbType { Date }
    internal sealed class MySqlException(int number) : Exception { public int Number => number; }
    internal sealed class Parameter { public object? Value { get; set; } }
    internal sealed class Parameters : Dictionary<string, Parameter>
    {
        public void AddWithValue(string key, object? value) => Add(key, new Parameter { Value = value });
        public Parameter Add(string key, MySqlDbType type) { var p = new Parameter(); Add(key, p); return p; }
    }
    internal sealed class MySqlConnection : IDisposable
    {
        public MySqlTransaction BeginTransaction(IsolationLevel level)
        {
            if (level != IsolationLevel.ReadCommitted) throw new Exception("Isolation changed");
            State.Throw("begin"); return new();
        }
        public void Dispose() { State.ConnectionsDisposed++; State.Throw("connection-dispose"); }
    }
    internal sealed class MySqlTransaction : IDisposable
    {
        public void Commit() { State.Commits++; State.Throw("commit"); State.Row = State.Pending ?? State.Row; State.Pending = null; }
        public void Rollback() { State.Rollbacks++; State.Throw("rollback"); State.Pending = null; }
        public void Dispose() { State.TransactionsDisposed++; State.Throw("transaction-dispose"); }
    }
    internal sealed class MySqlCommand(string sql, MySqlConnection connection, MySqlTransaction? transaction = null) : IDisposable
    {
        public Parameters Parameters { get; } = new();
        private void Log()
        {
            _ = connection; _ = transaction;
            State.Commands.Add((sql, Parameters.ToDictionary(p => p.Key, p => p.Value.Value)));
        }
        public MySqlDataReader ExecuteReader()
        {
            Log(); State.Throw("read");
            if (sql.Contains("FROM zimmer"))
                return new(State.Row == null ? new() : new() { Values(State.Row) });
            if (!sql.Contains("FROM reservierungen")) throw new Exception("Unknown read SQL");
            if (!sql.Contains("status = 'Eingecheckt'") || !sql.Contains("ORDER BY id FOR UPDATE")) throw new Exception("Protection changed");
            var rows = new List<Dictionary<string, object?>>();
            if (State.Occupied) rows.Add(new() { ["id"] = 2, ["status"] = "Eingecheckt" });
            if (State.Future && sql.Contains("@Today"))
            {
                if (!sql.Contains("abreise_datum >= @Today") || !Equals(Parameters["@Today"].Value, DateTime.Today)) throw new Exception("Date protection changed");
                rows.Add(new() { ["id"] = 3, ["status"] = "Reserviert" });
            }
            return new(rows);
        }
        public int ExecuteNonQuery()
        {
            Log(); State.Writes++; State.Throw("update");
            if (State.Fault.Split(',').Contains("1062")) throw new MySqlException(1062);
            if (State.Row == null) throw new Exception("Write without locked row");
            if (State.Affected == 1)
            {
                if (sql.Contains("SET geloescht_am"))
                    State.Pending = State.Row with { DateDelete = (DateTime)Parameters["@DateDelete"].Value! };
                else
                    State.Pending = State.Row with {
                        RoomNumber = Convert.ToString(Parameters["@RoomNumber"].Value, CultureInfo.InvariantCulture)!,
                        Type = (string)Parameters["@Type"].Value!, Capacity = (int)Parameters["@Capacity"].Value!,
                        PricePerNight = (decimal)Parameters["@PricePerNight"].Value!, Status = (string)Parameters["@Status"].Value!,
                        Description = Parameters["@Description"].Value as string,
                        ImagePath = Parameters["@ImagePath"].Value as string ?? State.Row.ImagePath };
            }
            return State.Affected;
        }
        public object ExecuteScalar() { Log(); State.Throw("insert"); return 9; }
        public void Dispose()
        {
            State.Throw("command-dispose");
            if (State.Writes > 0) State.Throw("write-command-dispose");
        }
        private static Dictionary<string, object?> Values(RoomOriginalValues r) => new()
        {
            ["id"] = r.Id, ["zimmer_nummer"] = r.RoomNumber, ["zimmer_typ"] = r.Type,
            ["kapazitaet"] = r.Capacity, ["preis_pro_nacht"] = r.PricePerNight, ["status"] = r.Status,
            ["beschreibung"] = r.Description, ["bild_pfad"] = r.ImagePath,
            ["hinzugefuegt_am"] = r.DateAdded, ["geloescht_am"] = r.DateDelete
        };
    }
    internal sealed class MySqlDataReader(List<Dictionary<string, object?>> rows) : IDisposable
    {
        private int index = -1;
        private string[] Keys => rows[index].Keys.ToArray();
        public bool Read() { State.Throw("reader-read"); return ++index < rows.Count; }
        public int GetOrdinal(string name) => Array.IndexOf(Keys, name);
        public bool IsDBNull(int index) => rows[this.index][Keys[index]] == null;
        public string GetString(string name) => (string)rows[index][name]!;
        public int GetInt32(string name) => Convert.ToInt32(rows[index][name], CultureInfo.InvariantCulture);
        public decimal GetDecimal(string name) => (decimal)rows[index][name]!;
        public DateTime GetDateTime(string name) => (DateTime)rows[index][name]!;
        public void Dispose() => State.Throw("reader-dispose");
    }
}
