using MySql.Data.MySqlClient;
using System.Data;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

internal enum CaseKind { LastTickets, Capacity, Status, Stale }
internal sealed record EventCase(string Name, CaseKind Kind, bool Ordered = false, bool AdminFirst = false)
{
    internal static readonly EventCase[] All =
    {
        new("buyers-last-tickets", CaseKind.LastTickets), new("buy-versus-capacity", CaseKind.Capacity),
        new("buy-versus-deactivate", CaseKind.Status), new("stale-admin-after-purchase", CaseKind.Stale),
        new("buy-before-capacity", CaseKind.Capacity, true), new("capacity-before-buy", CaseKind.Capacity, true, true),
        new("buy-before-deactivate", CaseKind.Status, true), new("deactivate-before-buy", CaseKind.Status, true, true)
    };
    internal int Capacity => Kind == CaseKind.LastTickets ? 2 : Kind == CaseKind.Stale ? 10 : 4;
    internal string[] Keys => Kind == CaseKind.Stale ? new[] { "S", "A", "B" } : new[] { "A", "B" };
    internal Operation A => Kind == CaseKind.Stale ? Operation.Rename : Operation.Buy;
    internal Operation B => Kind switch { CaseKind.Capacity => Operation.Shrink, CaseKind.Status => Operation.Deactivate, _ => Operation.Buy };
    internal bool Permitted(string key, Operation operation) => key == "A" && operation == A || key == "B" && operation == B ||
        Kind == CaseKind.Stale && key == "S" && operation == Operation.Seed;
    internal bool Matches(EventSnapshot final, IReadOnlyList<EventWorkerState> workers, EventPlan plan)
    {
        if (!EventWorkerState.CanCleanup(true, true, Keys, workers) || workers.Any(w => !Permitted(w.Key, w.Operation))) return false;
        string Result(string key) => workers.Single(w => w.Key == key).Packet!.Status is "Success" or "CommittedWithCleanupFailure"
            ? "Success" : workers.Single(w => w.Key == key).Packet!.Status;
        var a = Result("A"); var b = Result("B"); var row = final.Row;
        if (row.Title != (Kind == CaseKind.Stale ? plan.Renamed : plan.Title)) return false;
        bool buyWon = a == "Success";
        return Kind switch
        {
            CaseKind.LastTickets => (a == "Success" && b == "InsufficientTickets" || a == "InsufficientTickets" && b == "Success") && row.Sold == 2 && row.Max == 2 && row.Status == "Aktiv",
            CaseKind.Capacity => (!Ordered || buyWon != AdminFirst) && row.Status == "Aktiv" &&
                (buyWon && b == "CapacityBelowSold" && row.Sold == 2 && row.Max == 4 || a == "InsufficientTickets" && b == "Success" && row.Sold == 0 && row.Max == 1),
            CaseKind.Status => (!Ordered || buyWon != AdminFirst) && b == "Success" && row.Status == "Inaktiv" && row.Max == 4 &&
                (buyWon && row.Sold == 2 || a == "EventNotActive" && row.Sold == 0),
            CaseKind.Stale => Result("S") == "Success" && a == "Success" && b == "Success" && row.Sold == 7 && row.Max == 10 && row.Status == "Aktiv",
            _ => false
        };
    }
}

internal static class EventScenario
{
    internal static bool IsEventLock(string sql, int id) => sql.Contains("verkaufte_tickets", StringComparison.OrdinalIgnoreCase) &&
        sql.Contains("erstellt_am", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(sql,
            @"\A\s*SELECT\s+id\s*,[\s\S]*\bFROM\s+veranstaltungen\s+WHERE\s+id\s*=\s*'?" + id.ToString(CultureInfo.InvariantCulture) +
            @"'?\s+FOR\s+UPDATE\s*;?\s*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        !sql.Trim().TrimEnd(';').Contains(';');
    internal static bool OrdersMatch(EventSnapshot snapshot, IReadOnlyList<EventWorkerState> workers, EventPlan plan)
    {
        var buyers = workers.Where(w => w.Committed && EventWorkerState.Purchasing(w.Operation)).ToArray();
        return snapshot.Orders.Length == buyers.Length && buyers.All(w => snapshot.Orders.Count(o =>
            o.Code == w.Packet!.Code && o.Name == plan.Guest(w.Key) && o.Email == plan.Email(w.Key) &&
            o.Quantity == (w.Operation == Operation.Seed ? 5 : 2) && o.Unit == w.Packet.Unit && o.Total == w.Packet.Total) == 1);
    }
    internal static async Task RunSuite()
    {
        using (var conn = EventDatabaseGuard.Open()) EventDatabaseGuard.Preflight(conn);
        Console.WriteLine("TARGET verified: nest_integration_test; nest_test_runner@localhost; localhost:3306; InnoDB; isolated DML grants.");
        foreach (var scenario in EventCase.All) await Run(scenario); // Any failure stops the suite, never retries.
        Console.WriteLine("PASS: eight event scenarios and scoped cleanup confirmed.");
    }
    private static async Task Run(EventCase scenario)
    {
        var fixture = new EventFixture(EventPlan.New(scenario));
        var workers = new List<EventWorker>(); var sessions = new HashSet<int>();
        EventSnapshot? verified = null; bool finalVerified = false;
        Exception? failure = null;
        Console.WriteLine($"RUN {scenario.Name}; marker={fixture.Plan.Marker}");
        try
        {
            fixture.Create();
            using var observer = EventDatabaseGuard.Open();
            var baseline = fixture.Initial ?? throw new InvalidOperationException();
            if (scenario.Kind == CaseKind.Stale)
            {
                var seed = EventWorker.Start(fixture, "S", Operation.Seed); workers.Add(seed);
                await seed.Ready(baseline);
                sessions.UnionWith(await Round(fixture, observer, new[] { seed })); await seed.Finish();
                Safety.Require(seed.State.Committed);
                using var read = EventDatabaseGuard.Open(); baseline = EventFixture.ReadOwned(read, fixture.Plan, fixture.Id);
                Safety.Require(baseline.Row.Sold == 5 && baseline.Orders.Length == 1);
            }
            var a = EventWorker.Start(fixture, "A", scenario.A); workers.Add(a);
            var b = EventWorker.Start(fixture, "B", scenario.B); workers.Add(b);
            await Task.WhenAll(a.Ready(baseline), b.Ready(baseline));
            if (scenario.Kind == CaseKind.Stale || scenario.Ordered)
            {
                // Explicit API completion order, NOT a claim about InnoDB FIFO scheduling.
                var first = scenario.Kind == CaseKind.Stale || scenario.AdminFirst ? b : a;
                var second = ReferenceEquals(first, a) ? b : a;
                sessions.UnionWith(await Round(fixture, observer, new[] { first })); await first.Finish();
                Safety.Require(first.State.Committed);
                sessions.UnionWith(await Round(fixture, observer, new[] { second })); await second.Finish();
            }
            else
            {
                sessions.UnionWith(await Round(fixture, observer, new[] { a, b }));
                await Task.WhenAll(a.Finish(), b.Finish());
            }
            Safety.Require(sessions.Count == workers.Count && workers.All(w => w.State.Reliable));
            await SessionsGone(observer, sessions);
            using (var conn = EventDatabaseGuard.Open())
            using (var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    verified = EventFixture.ReadOwned(conn, fixture.Plan, fixture.Id, tx);
                    Safety.Require(verified.Row.Created == baseline.Row.Created && verified.Row.Author == baseline.Row.Author &&
                        verified.Row.Date == baseline.Row.Date && verified.Row.Time == baseline.Row.Time);
                    Safety.Require(scenario.Matches(verified, workers.Select(w => w.State).ToArray(), fixture.Plan) &&
                        OrdersMatch(verified, workers.Select(w => w.State).ToArray(), fixture.Plan));
                    Safety.Require(baseline.Orders.All(old => verified.Orders.Contains(old))); // No historical order mutation.
                }
                finally { tx.Rollback(); }
            }
            finalVerified = true;
            Console.WriteLine($"FINAL marker={fixture.Plan.Marker}; event={fixture.Id}; sold={verified.Row.Sold}; max={verified.Row.Max}; status={verified.Row.Status}; orderIds={string.Join(',', verified.Orders.Select(o => o.Id))}; worker sessions gone; row lock probe released.");
            if (workers.Any(w => !w.State.Strict)) failure = new InvalidOperationException(); // Cleanup failure never strict PASS.
        }
        catch (Exception ex) { failure ??= ex; }
        finally
        {
            foreach (var worker in workers)
            {
                try { await worker.StopOwnWorker(); }
                catch (Exception ex) { failure ??= ex; worker.State.StopUnconfirmed(); }
            }
            var states = workers.Select(w => w.State).ToArray();
            if (verified != null && EventWorkerState.CanCleanup(fixture.CreatedConfirmed, finalVerified, scenario.Keys, states))
            {
                try { fixture.Cleanup(verified, states, finalVerified); }
                catch (Exception ex) { failure ??= ex; fixture.Report(); }
            }
            else { failure ??= new InvalidOperationException(); fixture.Report(); }
            foreach (var worker in workers)
            {
                worker.Report();
                try { worker.Dispose(); } catch (Exception ex) { failure ??= ex; }
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS " + scenario.Name);
    }
    private static async Task<int[]> Round(EventFixture fixture, MySqlConnection observer, IReadOnlyList<EventWorker> workers)
    {
        using var conn = EventDatabaseGuard.Open();
        using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
        bool observed = false;
        try
        {
            EventFixture.ReadOwned(conn, fixture.Plan, fixture.Id, tx); // Coordinator owns parent row lock before GO.
            await Task.WhenAll(workers.Select(w => w.Go()));
            var ids = await Observe(observer, fixture.Id, conn.ServerThread, workers.Count);
            observed = true;
            Console.WriteLine($"OBSERVED event={fixture.Id}; FOR UPDATE sessions={string.Join(',', ids)}; coordinator lock still held. PROCESSLIST is NOT an InnoDB wait graph or FIFO proof.");
            return ids;
        }
        finally
        {
            tx.Rollback();
            Console.WriteLine($"RELEASED coordinator event={fixture.Id}; required queries observed={observed}");
        }
    }
    private static async Task<int[]> Observe(MySqlConnection conn, int id, int gate, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            using var cmd = EventDatabaseGuard.Command("SELECT ID,INFO FROM information_schema.PROCESSLIST WHERE USER='nest_test_runner' AND DB=DATABASE() AND ID<>CONNECTION_ID() AND ID<>@gate", conn);
            cmd.Parameters.AddWithValue("@gate", gate);
            var ids = new HashSet<int>();
            using (var reader = cmd.ExecuteReader()) while (reader.Read())
                if (!reader.IsDBNull(1) && IsEventLock(reader.GetString(1), id)) ids.Add(reader.GetInt32(0));
            if (ids.Count == count) return ids.Order().ToArray();
            Safety.Require(ids.Count < count);
            await Task.Delay(40, timeout.Token); // Poll a condition, never infer locking from elapsed time.
        }
    }
    private static async Task SessionsGone(MySqlConnection conn, IReadOnlyCollection<int> ids)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            using var cmd = EventDatabaseGuard.Command("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER='nest_test_runner' AND ID IN (@a,@b,@s)", conn);
            var values = ids.ToArray();
            cmd.Parameters.AddWithValue("@a", values[0]); cmd.Parameters.AddWithValue("@b", values.ElementAtOrDefault(1));
            cmd.Parameters.AddWithValue("@s", values.ElementAtOrDefault(2));
            if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return;
            await Task.Delay(40, timeout.Token);
        }
    }
}
