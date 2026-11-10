using Nest.Models;
using System.Collections.Concurrent;
using System.Data;
using System.Windows;

namespace StayTests
{
    public sealed record Booking(int Id, string Code, int RoomId, string Status);
    public static class State
    {
        public static readonly object Gate = new(), RoomGate = new();
        public static readonly Dictionary<string, Booking> Bookings = new();
        public static string Fault = "", RoomStatus = "";
        public static bool Deleted, RoomExists;
        public static int Opens, Begins, Updates, RoomUpdates, Commits, Rollbacks, ConnectionsDisposed, TransactionsDisposed, ReservationReads, RoomReads, RoomAttempts;
        public static ConcurrentQueue<(string Sql, Dictionary<string, object?> Parameters)> Commands = new();
        public static ConcurrentQueue<string> Events = new();
        public static ConcurrentDictionary<string, Exception> Errors = new();
        public static ManualResetEventSlim FirstLocked = new(false), ReleaseFirst = new(false), SecondAttempt = new(false);
        public static void Reset(string status, string room)
        {
            Fault = ""; RoomStatus = room; Deleted = false; RoomExists = true;
            Bookings.Clear(); Bookings["TEST"] = new(1, "TEST", 1, status);
            Opens = Begins = Updates = RoomUpdates = Commits = Rollbacks = ConnectionsDisposed = TransactionsDisposed = ReservationReads = RoomReads = RoomAttempts = 0;
            Commands = new(); Events = new(); Errors = new(); Dialog.Messages = new();
            FirstLocked = new(false); ReleaseFirst = new(false); SecondAttempt = new(false);
        }
        public static void Throw(string fault)
        {
            if (!Fault.Split(',').Contains(fault)) return;
            var error = new InvalidOperationException("PRIVATE " + fault);
            Errors[fault] = error;
            throw error;
        }
    }
    public static class Dialog
    {
        public static ConcurrentQueue<(string Text, string Title, MessageBoxButton Button, MessageBoxImage Image)> Messages = new();
        public static MessageBoxResult Show(string text, string title, MessageBoxButton button, MessageBoxImage image)
        {
            if (image == MessageBoxImage.Information) State.Throw("ui");
            Messages.Enqueue((text, title, button, image));
            return MessageBoxResult.Yes;
        }
    }
}
namespace StayTests.Data
{
    public static class DB
    {
        public static Fakes.MySqlConnection GetConnection() { State.Throw("create"); return new(); }
        public static void OpenConnection(Fakes.MySqlConnection connection) => connection.Open();
    }
    public sealed class GastRepository
    {
        public int FindOrAdd(Guest guest, Fakes.MySqlConnection connection, Fakes.MySqlTransaction transaction) => throw new NotSupportedException();
    }
    public sealed class RoomRepository
    {
        public List<Room> GetAll()
        {
            State.RoomReads++; State.Events.Enqueue("rooms-read"); State.Throw("rooms-load");
            return State.Deleted || !State.RoomExists ? new() : new() { new Room { Id = 1, RoomNumber = 101, Type = "Standard", Status = State.RoomStatus, Capacity = 2, PricePerNight = 50 } };
        }
    }
}
namespace StayTests.Fakes
{
    public enum MySqlDbType { Date, VarChar, Decimal, Int32 }
    public sealed class Parameter { public object? Value { get; set; } }
    public sealed class Parameters : Dictionary<string, Parameter>
    {
        public void AddWithValue(string key, object? value) => Add(key, new Parameter { Value = value });
        public Parameter Add(string key, MySqlDbType type) { var parameter = new Parameter(); Add(key, parameter); return parameter; }
    }
    public sealed class MySqlConnection : IDisposable
    {
        public void Open() { Interlocked.Increment(ref State.Opens); State.Throw("open"); }
        public MySqlTransaction BeginTransaction(IsolationLevel level)
        {
            State.Throw("begin"); if (level != IsolationLevel.ReadCommitted) throw new Exception("Isolation changed");
            Interlocked.Increment(ref State.Begins); return new(this);
        }
        public void Dispose() { Interlocked.Increment(ref State.ConnectionsDisposed); State.Throw("connection-dispose"); }
    }
    public sealed class MySqlTransaction : IDisposable
    {
        public readonly MySqlConnection Connection;
        public bool RoomLocked, ReservationLocked, Completed;
        public Booking? ChangedBooking;
        public string? ChangedRoom;
        public MySqlTransaction(MySqlConnection connection) => Connection = connection;
        public void LockRoom()
        {
            int attempt = Interlocked.Increment(ref State.RoomAttempts);
            if (attempt == 2) State.SecondAttempt.Set();
            if (!Monitor.TryEnter(State.RoomGate, 10000)) throw new Exception("Room lock timeout");
            RoomLocked = true; State.Events.Enqueue("room-locked");
            if (State.Fault == "concurrent" && attempt == 1)
            {
                State.FirstLocked.Set();
                if (!State.ReleaseFirst.Wait(10000)) throw new Exception("Concurrency timeout");
            }
        }
        public void Commit()
        {
            Interlocked.Increment(ref State.Commits); State.Throw("commit-before");
            lock (State.Gate)
            {
                if (ChangedBooking != null) State.Bookings[ChangedBooking.Code] = ChangedBooking;
                if (ChangedRoom != null) State.RoomStatus = ChangedRoom;
            }
            Completed = true; State.Throw("commit-after"); State.Events.Enqueue("committed");
        }
        public void Rollback()
        {
            Interlocked.Increment(ref State.Rollbacks); State.Throw("rollback");
            if (Completed) throw new InvalidOperationException("PRIVATE completed");
            ChangedBooking = null; ChangedRoom = null; Completed = true;
        }
        public void Dispose()
        {
            Interlocked.Increment(ref State.TransactionsDisposed);
            if (RoomLocked) { RoomLocked = false; Monitor.Exit(State.RoomGate); }
            State.Throw("transaction-dispose");
        }
    }
    public sealed class MySqlDataReader : IDisposable
    {
        private readonly List<Dictionary<string, object?>> rows;
        private int index = -1;
        public MySqlDataReader(IEnumerable<Dictionary<string, object?>> values) => rows = values.ToList();
        public bool Read() => ++index < rows.Count;
        public string GetString(string name) => Convert.ToString(rows[index][name]) ?? throw new Exception("Null string");
        public int GetInt32(string name) => Convert.ToInt32(rows[index][name]);
        public decimal GetDecimal(string name) => Convert.ToDecimal(rows[index][name]);
        public DateTime GetDateTime(string name) => (DateTime)(rows[index][name] ?? throw new Exception());
        public int GetOrdinal(string name) => rows[index].Keys.ToList().IndexOf(name);
        public bool IsDBNull(int ordinal) => rows[index].Values.ElementAt(ordinal) is null or DBNull;
        public void Dispose() { }
    }
    public sealed class MySqlCommand : IDisposable
    {
        private readonly string sql;
        private readonly MySqlTransaction? transaction;
        public Parameters Parameters { get; } = new();
        public MySqlCommand(string sql, MySqlConnection connection, MySqlTransaction? transaction = null)
        {
            this.sql = sql; this.transaction = transaction;
            if (transaction != null && !ReferenceEquals(transaction.Connection, connection)) throw new Exception("Connection changed");
        }
        private void Record() => State.Commands.Enqueue((sql.Replace("\r\n", "\n"), Parameters.ToDictionary(p => p.Key, p => p.Value.Value)));
        private T Value<T>(string key) => (T)(Parameters[key].Value ?? throw new Exception("Missing parameter"));
        public object? ExecuteScalar()
        {
            Record();
            if (sql.Contains("SELECT zimmer_id"))
            {
                State.Throw("candidate"); lock (State.Gate) return State.Bookings.TryGetValue(Value<string>("@code"), out var booking) ? booking.RoomId : null;
            }
            if (transaction == null || !transaction.RoomLocked || !transaction.ReservationLocked) throw new Exception("Occupancy before locks");
            State.Events.Enqueue("occupancy-read"); State.Throw("occupancy");
            lock (State.Gate) return State.Bookings.Values.Where(b => b.RoomId == Value<int>("@RoomId") && b.Id != Value<int>("@ReservationId") && b.Status == "Eingecheckt").OrderBy(b => b.Id).Select(b => (object)b.Id).FirstOrDefault();
        }
        public MySqlDataReader ExecuteReader()
        {
            Record();
            if (transaction == null)
            {
                State.ReservationReads++; State.Events.Enqueue("reservations-read"); State.Throw("reservations-load");
                lock (State.Gate) return new(State.Bookings.Values.Select(b => new Dictionary<string, object?>
                {
                    ["reservierungs_code"] = b.Code, ["guest"] = "Test Guest", ["zimmer_nummer"] = "101", ["zimmer_typ"] = "Standard",
                    ["anreise_datum"] = DateTime.Today, ["abreise_datum"] = DateTime.Today.AddDays(1), ["naechte"] = 1,
                    ["erwachsene"] = 1, ["kinder"] = 0, ["gesamtbetrag"] = 100m, ["gezahlter_betrag"] = 0m, ["zahlungsart"] = "Bar", ["status"] = b.Status
                }));
            }
            if (!sql.Contains("FOR UPDATE")) throw new Exception("Lock removed");
            if (sql.Contains("FROM zimmer"))
            {
                transaction.LockRoom(); State.Throw("room-lock");
                return new(State.RoomExists ? new[] { new Dictionary<string, object?> { ["id"] = 1, ["status"] = State.RoomStatus, ["geloescht_am"] = State.Deleted ? DateTime.Today : DBNull.Value } } : Array.Empty<Dictionary<string, object?>>());
            }
            if (!transaction.RoomLocked) throw new Exception("Reservation before room");
            transaction.ReservationLocked = true; State.Events.Enqueue("reservation-locked"); State.Throw("reservation-lock");
            lock (State.Gate)
            {
                if (!State.Bookings.TryGetValue(Value<string>("@code"), out var b) || State.Fault == "missing-after-candidate") return new(Array.Empty<Dictionary<string, object?>>());
                return new(new[] { new Dictionary<string, object?> { ["id"] = b.Id, ["reservierungs_code"] = State.Fault == "code-changed" ? "OTHER" : b.Code,
                    ["zimmer_id"] = State.Fault == "room-changed" ? 2 : b.RoomId, ["status"] = State.Fault == "status-changed" ? "Storniert" : b.Status } });
            }
        }
        public int ExecuteNonQuery()
        {
            Record(); Interlocked.Increment(ref State.Updates);
            if (transaction == null || !transaction.RoomLocked || !transaction.ReservationLocked) throw new Exception("UPDATE before locks");
            if (sql.Contains("UPDATE reservierungen"))
            {
                State.Throw("first-sql");
                if (State.Fault.Split(',').Contains("first-zero")) return 0;
                if (State.Fault == "first-two") return 2;
                lock (State.Gate)
                {
                    var booking = State.Bookings[Value<string>("@code")];
                    transaction.ChangedBooking = booking with { Status = Value<string>("@TargetStatus") }; return 1;
                }
            }
            Interlocked.Increment(ref State.RoomUpdates); State.Throw("second-sql");
            if (State.Fault == "second-zero") return 0;
            if (State.Fault == "second-two") return 2;
            transaction.ChangedRoom = Value<string>("@TargetRoomStatus"); return 1;
        }
        public void Dispose() { }
    }
}
