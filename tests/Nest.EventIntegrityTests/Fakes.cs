using System.Data;
using System.Windows;

namespace EventTests
{
    // In-memory transport, NOT MariaDB or a model of its wait graph.
    internal sealed record Row(int Id = 1, string Title = "Test event", int? Sold = 5,
        int? Max = 10, string Status = "Aktiv", decimal Price = 12.50m,
        DateTime Date = default, string? Description = null, string? Location = "Lobby",
        string? Image = null, int? Author = 9, DateTime Created = default)
    {
        public Dictionary<string, object?> Values() => new()
        {
            ["id"] = Id, ["titel"] = Title, ["verkaufte_tickets"] = Sold,
            ["maximale_tickets"] = Max, ["status"] = Status, ["ticket_preis"] = Price,
            ["veranstaltungs_datum"] = Date == default ? DateTime.Today.AddDays(5) : Date,
            ["veranstaltungs_zeit"] = TimeSpan.FromHours(19), ["beschreibung"] = Description,
            ["ort"] = Location, ["bild_url"] = Image, ["erstellt_von"] = Author,
            ["erstellt_am"] = Created == default ? DateTime.Today.AddDays(-10) : Created
        };
    }
    internal static class State
    {
        internal static readonly object RowGate = new();
        private static readonly object LogGate = new();
        internal static Row? Row;
        internal static int Orders, Quantity, Writes, Commits, Rollbacks, Connections, Closed;
        internal static HashSet<string> Faults = new();
        internal static bool PrimaryRaised;
        internal static Dictionary<string, int> Affected = new();
        internal static readonly List<string> Trace = new();
        internal static readonly List<string> Sql = new();
        internal static readonly Exception Primary = new InvalidOperationException("synthetic SQL failure");
        internal static void Reset(Row? row)
        {
            Row = row; Orders = Quantity = Writes = Commits = Rollbacks = Connections = Closed = 0;
            Faults = new(); PrimaryRaised = false; Affected = new(); Trace.Clear(); Sql.Clear(); Dialog.Messages.Clear(); Primary.Data.Clear();
        }
        internal static void Point(string point)
        {
            lock (LogGate) Trace.Add(point);
            if (Faults.Contains(point))
            {
                if (point is "insert" or "sale" or "admin" or "read") { PrimaryRaised = true; throw Primary; }
                throw new InvalidOperationException(point);
            }
        }
        internal static void Log(string sql) { lock (LogGate) Sql.Add(sql); }
        internal static int Rows(string kind) => Affected.GetValueOrDefault(kind, 1);
    }
    internal static class Dialog
    {
        internal static readonly List<string> Messages = new();
        public static MessageBoxResult Show(string text, string title = "", MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None)
        { Messages.Add(text); return MessageBoxResult.OK; }
    }
}
namespace EventTests.Data
{
    internal static class DB
    {
        internal static Fakes.MySqlConnection GetConnection() { State.Point("create"); return new(); }
        internal static void OpenConnection(Fakes.MySqlConnection conn) { State.Point("open"); }
    }
}
namespace EventTests.Fakes
{
    internal enum MySqlDbType { Int32, VarChar, Decimal, DateTime }
    internal sealed class Parameter { public object? Value { get; set; } }
    internal sealed class Parameters : Dictionary<string, Parameter>
    {
        public Parameter Add(string name, MySqlDbType type) { var p = new Parameter(); Add(name, p); return p; }
        public void AddWithValue(string name, object? value) => Add(name, new Parameter { Value = value });
    }
    internal sealed class MySqlConnection : IDisposable
    {
        public MySqlConnection() => Interlocked.Increment(ref State.Connections);
        public MySqlTransaction BeginTransaction(IsolationLevel level)
        {
            if (level != IsolationLevel.ReadCommitted) throw new Exception("Isolation changed");
            State.Point("begin"); return new();
        }
        public void Dispose() { Interlocked.Increment(ref State.Closed); State.Point("connection-dispose"); }
    }
    internal sealed class MySqlTransaction : IDisposable
    {
        internal Row? Pending;
        internal int NewOrders, NewQuantity;
        internal bool Locked;
        internal void Lock()
        {
            if (Locked) return;
            Monitor.Enter(State.RowGate); Locked = true; Pending = State.Row; State.Point("lock");
        }
        private void Release() { if (Locked) { Locked = false; Monitor.Exit(State.RowGate); } }
        public void Commit()
        {
            State.Commits++; State.Point("commit");
            State.Row = Pending; State.Orders += NewOrders; State.Quantity += NewQuantity;
            NewOrders = NewQuantity = 0; Release(); State.Point("commit-after");
        }
        public void Rollback() { State.Rollbacks++; State.Point("rollback"); NewOrders = NewQuantity = 0; Release(); }
        public void Dispose() { Release(); State.Point("transaction-dispose"); }
    }
    internal sealed class MySqlCommand(string sql, MySqlConnection connection, MySqlTransaction? transaction = null) : IDisposable
    {
        public Parameters Parameters { get; } = new();
        public long LastInsertedId => 1;
        private object? Value(string name) => Parameters[name].Value is DBNull ? null : Parameters[name].Value;
        public MySqlDataReader ExecuteReader()
        {
            _ = connection; State.Log(sql);
            if (sql.Contains("FOR UPDATE"))
            {
                if (transaction == null) throw new Exception("Lock outside transaction");
                transaction.Lock();
            }
            State.Point("read");
            if (!sql.Contains("FROM veranstaltungen")) throw new Exception("Unrecognized read");
            if (!sql.Contains("FOR UPDATE")) State.Point("list-read");
            var row = transaction?.Locked == true ? transaction.Pending : State.Row;
            if (Parameters.ContainsKey("@EventId") && row?.Id != (int)Value("@EventId")!) row = null;
            if (sql.Contains("status = 'Aktiv'") && row?.Status != "Aktiv") row = null;
            return new(row == null ? new() : new() { row.Values() });
        }
        public int ExecuteNonQuery()
        {
            State.Log(sql); State.Writes++;
            if (sql.Contains("INSERT INTO veranstaltungen"))
            {
                State.Point("add");
                if (!Equals(Value("@verkaufte_tickets"), 0)) throw new Exception("Nonzero initial sales");
                return 1;
            }
            if (transaction?.Locked != true || transaction.Pending == null) throw new Exception("Write without row lock");
            string kind = sql.Contains("INSERT INTO ticketbestellungen") ? "insert"
                : sql.Contains("COALESCE(verkaufte_tickets") ? "sale" : "admin";
            State.Point(kind);
            var affected = State.Rows(kind);
            if (affected != 1) return affected;
            if (kind == "insert") { transaction.NewOrders++; transaction.NewQuantity += (int)Value("@Quantity")!; }
            else if (kind == "sale") transaction.Pending = transaction.Pending with
                { Sold = (transaction.Pending.Sold ?? 0) + (int)Value("@Quantity")! };
            else
            {
                if (sql.Contains("verkaufte_tickets") || sql.Contains("erstellt_")) throw new Exception("Admin writes protected fields");
                transaction.Pending = transaction.Pending with
                {
                    Title = (string)Value("@Title")!, Max = (int)Value("@MaxTickets")!,
                    Status = (string)Value("@Status")!, Price = (decimal)Value("@TicketPrice")!,
                    Date = (DateTime)Value("@EventDate")!, Description = (string?)Value("@Description"),
                    Location = (string?)Value("@Location"), Image = (string?)Value("@ImageUrl")
                };
            }
            return 1; // matched rows, including no-op (UseAffectedRows=false).
        }
        public void Dispose()
        {
            State.Point("command-dispose");
            if (State.PrimaryRaised) State.Point("command-dispose-after-error");
        }
    }
    internal sealed class MySqlDataReader(List<Dictionary<string, object?>> rows) : IDisposable
    {
        private int index = -1;
        private string[] Keys => rows[index].Keys.ToArray();
        public bool Read() { State.Point("reader-read"); return ++index < rows.Count; }
        public int GetOrdinal(string name) => Array.IndexOf(Keys, name);
        public bool IsDBNull(int ordinal) => rows[index][Keys[ordinal]] == null;
        public string GetString(string name) => (string)rows[index][name]!;
        public int GetInt32(string name) => (int)rows[index][name]!;
        public decimal GetDecimal(string name) => (decimal)rows[index][name]!;
        public DateTime GetDateTime(string name) => (DateTime)rows[index][name]!;
        public TimeSpan GetTimeSpan(int ordinal) => (TimeSpan)rows[index][Keys[ordinal]]!;
        public TimeSpan GetTimeSpan(string name) => (TimeSpan)rows[index][name]!;
        public void Dispose() => State.Point("reader-dispose");
    }
}
