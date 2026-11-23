using MySql.Data.MySqlClient;
using Nest.Data;
using System.Data;

internal sealed record EventPlan(string Token, EventCase Case)
{
    internal string Marker => "Nest-E52-" + Token;
    internal string Description => Marker + "/" + Case.Name;
    internal string Owner => "nest-integration://event52/" + Token;
    internal string Title => Marker + " event";
    internal string Renamed => Marker + " renamed";
    internal string Guest(string key) => Marker + "/" + key;
    internal string Email(string key) => Token + "." + key + "@example.invalid";
    internal const string Phone = "event-test-only";
    internal const decimal Price = 12.50m;
    internal static EventPlan New(EventCase scenario) => new(Guid.NewGuid().ToString("N"), scenario);
    internal static EventPlan Parse(string marker, string scenario)
    {
        const string prefix = "Nest-E52-";
        Safety.Require(marker.StartsWith(prefix, StringComparison.Ordinal));
        var token = marker[prefix.Length..];
        Safety.Require(Guid.TryParseExact(token, "N", out var id) && id.ToString("N") == token);
        return new(token, EventCase.All.Single(c => c.Name == scenario));
    }
}

internal sealed record EventRow(int Id, string Title, string Description, string Owner, DateTime Date,
    TimeSpan Time, string Location, decimal Price, int Max, int Sold, string Status, int? Author, DateTime Created)
{
    internal EventUpdateRequest Request(Operation operation) => new(Id,
        operation == Operation.Rename ? Title.Replace(" event", " renamed", StringComparison.Ordinal) : Title,
        Description, Date, Time, Location, Price, operation == Operation.Shrink ? 1 : Max,
        Owner, operation == Operation.Deactivate ? "Inaktiv" : Status);
}
internal sealed record OrderRow(int Id, int EventId, string Code, string Name, string Email, string Phone,
    int Quantity, decimal Unit, decimal Total, int? Reservation, DateTime Ordered);
internal sealed record EventSnapshot(EventRow Row, OrderRow[] Orders)
{
    internal string Fingerprint => Safety.Fingerprint(this);
}

internal sealed class EventFixture(EventPlan plan)
{
    internal EventPlan Plan { get; } = plan;
    internal int Id { get; private set; }
    internal bool CreatedConfirmed { get; private set; }
    internal bool CleanupConfirmed { get; private set; }
    internal EventSnapshot? Initial { get; private set; }
    private const string Projection = @"SELECT id,titel,beschreibung,bild_url,veranstaltungs_datum,veranstaltungs_zeit,
        ort,ticket_preis,maximale_tickets,verkaufte_tickets,status,erstellt_von,erstellt_am
        FROM veranstaltungen WHERE id=@id";
    internal void Create()
    {
        // A creation exception (including disposal after Commit) never enables cleanup.
        using (var conn = EventDatabaseGuard.Open())
        {
            using (var collision = EventDatabaseGuard.Command("SELECT COUNT(*) FROM veranstaltungen WHERE BINARY beschreibung=BINARY @marker OR BINARY bild_url=BINARY @owner", conn))
            {
                collision.Parameters.AddWithValue("@marker", Plan.Description); collision.Parameters.AddWithValue("@owner", Plan.Owner);
                Safety.Require(Convert.ToInt32(collision.ExecuteScalar()) == 0);
            }
            using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
            try
            {
                using (var cmd = EventDatabaseGuard.Command(@"INSERT INTO veranstaltungen
                    (titel,beschreibung,bild_url,veranstaltungs_datum,veranstaltungs_zeit,ort,ticket_preis,maximale_tickets,
                     verkaufte_tickets,status,erstellt_von,erstellt_am)
                    VALUES (@title,@marker,@owner,@date,@time,'EventIntegrity',@price,@max,0,'Aktiv',NULL,@created)", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@title", Plan.Title); cmd.Parameters.AddWithValue("@marker", Plan.Description);
                    cmd.Parameters.AddWithValue("@owner", Plan.Owner); cmd.Parameters.AddWithValue("@date", DateTime.Today.AddDays(30));
                    cmd.Parameters.AddWithValue("@time", TimeSpan.FromHours(19)); cmd.Parameters.AddWithValue("@price", EventPlan.Price);
                    cmd.Parameters.AddWithValue("@max", Plan.Case.Capacity); cmd.Parameters.AddWithValue("@created", DateTime.Today);
                    Id = EventDatabaseGuard.WriteOne(cmd); Safety.Require(Id > 0);
                    Console.WriteLine($"FIXTURE allocated marker={Plan.Marker}; event={Id}; creation not yet confirmed.");
                }
                tx.Commit();
            }
            catch { try { tx.Rollback(); } catch { } throw; }
        }
        using (var validation = EventDatabaseGuard.Open())
        {
            Initial = ReadOwned(validation, Plan, Id);
            Safety.Require(Initial.Row.Sold == 0 && Initial.Row.Max == Plan.Case.Capacity && Initial.Orders.Length == 0 && Initial.Row.Title == Plan.Title);
        }
        CreatedConfirmed = true;
    }
    internal static bool Owned(EventRow row, EventPlan plan, int id) => id > 0 && row.Id == id &&
        row.Description == plan.Description && row.Owner == plan.Owner && row.Title is not null &&
        (row.Title == plan.Title || row.Title == plan.Renamed) && row.Location == "EventIntegrity" &&
        row.Price == EventPlan.Price && row.Date > DateTime.Today.AddDays(1) && row.Time == TimeSpan.FromHours(19) &&
        row.Author == null && row.Max is >= 1 and <= 10 && row.Sold >= 0 && row.Sold <= row.Max &&
        row.Status is "Aktiv" or "Inaktiv";
    internal static bool Owned(OrderRow order, EventPlan plan, int id) => order.Id > 0 && order.EventId == id &&
        EventWorkerState.ValidTicketCode(order.Code) && order.Reservation == null && order.Phone == EventPlan.Phone &&
        order.Unit == EventPlan.Price && order.Total == order.Unit * order.Quantity &&
        new[] { "A", "B", "S" }.Any(key => order.Name == plan.Guest(key) && order.Email == plan.Email(key) &&
            order.Quantity == (key == "S" ? 5 : 2));

    internal static EventSnapshot ReadOwned(MySqlConnection conn, EventPlan plan, int id, MySqlTransaction? tx = null)
    {
        EventRow row;
        using (var cmd = EventDatabaseGuard.Command(Projection + (tx == null ? "" : " FOR UPDATE"), conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader(); Safety.Require(reader.Read());
            row = new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDateTime(4),
                reader.GetTimeSpan(5), reader.GetString(6), reader.GetDecimal(7), reader.GetInt32(8), reader.GetInt32(9),
                reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.GetDateTime(12));
            Safety.Require(!reader.Read() && Owned(row, plan, id));
        }
        var orders = new List<OrderRow>();
        using (var cmd = EventDatabaseGuard.Command(@"SELECT id,veranstaltung_id,ticket_nummer,gast_name,gast_email,gast_telefon,
            anzahl,einzelpreis,gesamtpreis,reservierung_id,bestellt_am FROM ticketbestellungen WHERE veranstaltung_id=@id ORDER BY id" +
            (tx == null ? "" : " FOR UPDATE"), conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var order = new OrderRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9), reader.GetDateTime(10));
                Safety.Require(Owned(order, plan, id)); orders.Add(order);
            }
        }
        Safety.Require(orders.Sum(o => o.Quantity) == row.Sold && orders.Select(o => o.Code).Distinct(StringComparer.Ordinal).Count() == orders.Count);
        return new(row, orders.ToArray());
    }
    internal void Cleanup(EventSnapshot verified, IReadOnlyList<EventWorkerState> workers, bool finalVerified)
    {
        Safety.Require(EventWorkerState.CanCleanup(CreatedConfirmed, finalVerified, Plan.Case.Keys, workers) &&
            Plan.Case.Matches(verified, workers, Plan) && EventScenario.OrdersMatch(verified, workers, Plan));
        EventPlan.Parse(Plan.Marker, Plan.Case.Name);
        using var conn = EventDatabaseGuard.Open(); EventDatabaseGuard.Schema(conn);
        using (var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted))
        {
            try
            {
                var current = ReadOwned(conn, Plan, Id, tx); // Parent X lock before checking/deleting any child.
                Safety.Require(current.Fingerprint == verified.Fingerprint && EventScenario.OrdersMatch(current, workers, Plan));
                foreach (var order in current.Orders)
                {
                    using var cmd = EventDatabaseGuard.Command(@"DELETE FROM ticketbestellungen WHERE id=@id AND veranstaltung_id=@event
                        AND BINARY ticket_nummer=BINARY @code AND BINARY gast_name=BINARY @name AND BINARY gast_email=BINARY @email", conn, tx);
                    cmd.Parameters.AddWithValue("@id", order.Id); cmd.Parameters.AddWithValue("@event", Id);
                    cmd.Parameters.AddWithValue("@code", order.Code); cmd.Parameters.AddWithValue("@name", order.Name);
                    cmd.Parameters.AddWithValue("@email", order.Email); EventDatabaseGuard.WriteOne(cmd);
                }
                using (var cmd = EventDatabaseGuard.Command(@"DELETE FROM veranstaltungen WHERE id=@id AND BINARY beschreibung=BINARY @marker
                    AND BINARY bild_url=BINARY @owner AND BINARY titel=BINARY @title", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", Id); cmd.Parameters.AddWithValue("@marker", Plan.Description);
                    cmd.Parameters.AddWithValue("@owner", Plan.Owner); cmd.Parameters.AddWithValue("@title", current.Row.Title);
                    EventDatabaseGuard.WriteOne(cmd);
                }
                tx.Commit();
            }
            catch { try { tx.Rollback(); } catch { } throw; } // No retry, including uncertain cleanup Commit.
        }
        using (var cmd = EventDatabaseGuard.Command("SELECT COUNT(*) FROM veranstaltungen WHERE id=@id OR BINARY bild_url=BINARY @owner OR BINARY beschreibung=BINARY @marker", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id); cmd.Parameters.AddWithValue("@owner", Plan.Owner); cmd.Parameters.AddWithValue("@marker", Plan.Description);
            Safety.Require(Convert.ToInt32(cmd.ExecuteScalar()) == 0);
        }
        using (var cmd = EventDatabaseGuard.Command("SELECT COUNT(*) FROM ticketbestellungen WHERE veranstaltung_id=@id OR BINARY gast_name IN (@a,@b,@s)", conn))
        {
            cmd.Parameters.AddWithValue("@id", Id); cmd.Parameters.AddWithValue("@a", Plan.Guest("A"));
            cmd.Parameters.AddWithValue("@b", Plan.Guest("B")); cmd.Parameters.AddWithValue("@s", Plan.Guest("S"));
            Safety.Require(Convert.ToInt32(cmd.ExecuteScalar()) == 0);
        }
        CleanupConfirmed = true;
        Console.WriteLine($"CLEANUP confirmed marker={Plan.Marker}; event={Id}; orders={string.Join(',', verified.Orders.Select(o => o.Id))}; own IDs/markers absent.");
    }
    internal void Report() => Console.WriteLine($"RETAIN/INSPECT marker={Plan.Marker}; event={Id}; creationConfirmed={CreatedConfirmed}; cleanupConfirmed={CleanupConfirmed}. No automatic retry; inspect before any manual deletion.");
}
