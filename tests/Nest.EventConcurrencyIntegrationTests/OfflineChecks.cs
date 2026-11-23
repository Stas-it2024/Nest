using MySql.Data.MySqlClient;
using Nest.Data;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class OfflineChecks
{
    private static int checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("Offline check failed: " + name); checks++; }
    private static void Reject(Action action, string name)
    {
        bool rejected = false; try { action(); } catch { rejected = true; }
        Check(rejected, name);
    }
    internal static string Line(WorkerPacket packet) => "RESULT " + JsonSerializer.Serialize(packet);
    private static WorkerPacket Packet(string key, Operation operation, string status = "Success") =>
        EventWorkerState.Purchasing(operation) && status is "Success" or "CommittedWithCleanupFailure"
            ? new(key, operation.ToString(), status, key == "S" ? "TKT-20261118-ABCDEF012347" : key == "B" ? "TKT-20261118-ABCDEF012346" : "TKT-20261118-ABCDEF012345", EventPlan.Price,
                EventPlan.Price * (operation == Operation.Seed ? 5 : 2), 0)
            : new(key, operation.ToString(), status);
    private static EventWorkerState Completed(string key, Operation operation, string status = "Success")
    {
        var state = new EventWorkerState(key, operation); state.Complete(Line(Packet(key, operation, status)), 0, true); return state;
    }
    internal static void Run()
    {
        Check(EventDatabaseGuard.Offline, "offline guard set by entry before any work");
        var variables = new[] { IntegrationConnectionSettings.ModeVariable, IntegrationConnectionSettings.ConnectionVariable, EventDatabaseGuard.WriteVariable };
        var original = variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);
        try
        {
            Admission(); Protocol(); FixturesAndMatrices(); SchemaAndSource();
            Reject(() => EventDatabaseGuard.Open(), "offline blocks connection path");
            Reject(EventDatabaseGuard.ConfigureRunner, "offline blocks credential path");
            Check(EventDatabaseGuard.ConnectionsOpened == 0 && EventDatabaseGuard.CredentialsRead == 0, "no connections or credentials");
            DiagnosticChecks.Run();
            Console.WriteLine($"PASS: {checks} event integration offline checks; admission/protocol/fixtures/matrices/cleanup/source. No credentials read, connections opened, SQL or Repository operations executed.");
        }
        finally { foreach (var pair in original) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }
    private static void Admission()
    {
        var valid = new MySqlConnectionStringBuilder
        {
            Server = "localhost", Port = 3306, Database = "nest_integration_test", UserID = "nest_test_runner",
            Password = "non-secret-offline-placeholder", UseAffectedRows = false
        };
        void Configure(string? mode, string? flag, string? connection)
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, mode);
            Environment.SetEnvironmentVariable(EventDatabaseGuard.WriteVariable, flag);
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, connection);
        }
        foreach (var mode in new string?[] { null, "0", "true", "1" })
        foreach (var flag in new string?[] { null, "1", "hotel", "nest_integration_test" })
        {
            Configure(mode, flag, valid.ConnectionString);
            if (mode == "1" && flag == "nest_integration_test") { EventDatabaseGuard.RequireRunner(); Check(true, "all flags"); }
            else Reject(EventDatabaseGuard.RequireRunner, "mandatory flags");
        }
        Configure("1", "nest_integration_test", valid.ConnectionString);
        foreach (var change in new (string Key, object Value)[]
        {
            ("Database", "hotel"), ("Database", "other"), ("Server", "127.0.0.1"), ("Server", "remote.invalid"),
            ("Port", 3307u), ("User ID", "root"), ("User ID", "nest_test_app"), ("User ID", "other"),
            ("Password", ""), ("UseAffectedRows", true), ("Pooling", true)
        })
        {
            var altered = new MySqlConnectionStringBuilder(valid.ConnectionString) { [change.Key] = change.Value };
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, altered.ConnectionString);
            Reject(EventDatabaseGuard.RequireRunner, "unsafe setting " + change.Key);
            Check(Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable) == altered.ConnectionString, "no fallback replaces bad settings");
        }
        foreach (string? invalid in new[] { null, "invalid", "" })
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, invalid);
            Reject(EventDatabaseGuard.RequireRunner, "missing/invalid configuration");
        }
        Configure("1", "nest_integration_test", valid.ConnectionString);
        foreach (var args in new[] { Array.Empty<string>(), new[] { "--execute-b21" }, new[] { "--execute-b22" },
            new[] { "--execute-room-concurrency" }, new[] { "--worker" }, new[] { Program.Execute, "extra" } })
            Check(!Program.Controller(args) && !Program.Worker(args), "other entry cannot authorize writes");
        Check(Program.Controller(new[] { Program.Execute }), "controller flag");
        Check(Program.Worker(new[] { Program.Execute, "--worker", "marker", "1", "scenario", "Buy", "A" }), "worker also needs execute flag");
        const string usage = "GRANT USAGE ON *.* TO 'nest_test_runner'@'localhost'";
        const string scoped = "GRANT SELECT, INSERT, UPDATE, DELETE ON `nest_integration_test`.* TO 'nest_test_runner'@'localhost'";
        Check(EventDatabaseGuard.IsolatedGrants(new[] { usage, scoped }), "isolated DML rights");
        Check(!EventDatabaseGuard.IsolatedGrants(new[] { usage }), "missing rights");
        foreach (var bad in new[] { "GRANT SELECT ON `hotel`.* TO x", "GRANT ALL PRIVILEGES ON *.* TO x", "GRANT PROCESS ON *.* TO x",
            "GRANT CREATE ON `nest_integration_test`.* TO x", scoped + " WITH GRANT OPTION", "GRANT `some_role` TO x" })
            Check(!EventDatabaseGuard.IsolatedGrants(new[] { usage, scoped, bad }), "extra rights rejected");
    }
    private static void Protocol()
    {
        foreach (var op in Enum.GetValues<Operation>())
        {
            var key = op == Operation.Seed ? "S" : "A";
            foreach (var status in new[] { "Success", "CommittedWithCleanupFailure" })
            {
                var state = Completed(key, op, status);
                Check(state.Committed && state.Reliable && state.Strict == (status == "Success"), "confirmed result classification");
            }
        }
        foreach (var op in Enum.GetValues<Operation>())
        {
            var statuses = EventWorkerState.Purchasing(op) ? Enum.GetNames<TicketBookingStatus>() : Enum.GetNames<EventUpdateStatus>();
            foreach (var status in statuses.Where(s => s is not ("Success" or "CommittedWithCleanupFailure")))
                Check(Completed("A", op, status).Outcome == Outcome.BusinessRefusal, "typed business refusal");
        }
        foreach (var terminal in new[] { "COMMIT_UNCERTAIN", "ROLLBACK_UNCERTAIN", "TECHNICAL_ERROR" })
        foreach (int code in new[] { 0, 1, 2 })
        foreach (bool stopped in new[] { false, true })
        {
            var state = new EventWorkerState("A", Operation.Buy);
            state.Complete(Line(Packet("A", Operation.Buy, terminal)), code, stopped);
            Check(!state.Reliable && !state.Committed, "unknown result never becomes confirmed");
            state.Stopped(false); Check(!state.Reliable, "stopping does not resolve unknown transaction");
        }
        var good = Line(Packet("A", Operation.Buy));
        var uncertain = Line(Packet("A", Operation.Buy, "COMMIT_UNCERTAIN"));
        foreach (var text in new[] { "", "RESULT", "RESULT null", "RESULT {}", "RESULT {broken}", "debug\n" + good,
            good + "\n" + good, good + "\n" + uncertain, uncertain + "\n" + good,
            good.Replace("\"Status\":\"Success\"", "\"Status\":\"COMMIT_UNCERTAIN\",\"Status\":\"Success\""),
            good.Replace("\"Status\":\"Success\"", "\"Status\" : \"COMMIT_UNCERTAIN\",\"Status\":\"Success\""),
            good.Replace("\"Key\":\"A\"", "\"Key\":\"B\""), good.Replace("\"Operation\":\"Buy\"", "\"Operation\":\"Shrink\""),
            good.Replace("\"Status\":\"Success\"", "\"Status\":\"0\""), good.Replace("ABCDEF012345", "bad"),
            good.Replace("\"Remaining\":0", "\"Remaining\":-1"), good.Replace("\"Code\":\"TKT-20261118-ABCDEF012345\"", "\"Code\":null"),
            good.Replace("\"Remaining\":0", "\"Remaining\":0,\"Other\":1") })
        {
            var state = new EventWorkerState("A", Operation.Buy); state.Complete(text, 0, true);
            Check(!state.Reliable, "lost/malformed/duplicate result rejected");
        }
        foreach (bool stopped in new[] { false, true })
        foreach (int code in new[] { 0, 1, 2 })
        {
            var state = new EventWorkerState("A", Operation.Buy); state.Complete(good, code, stopped);
            Check(state.Reliable == (stopped && code == 0), "natural exit and exit code required");
        }
        foreach (var error in new Exception[] { new TimeoutException(), new InvalidOperationException() })
        {
            var state = new EventWorkerState("A", Operation.Buy); state.Fail(error); state.Complete(good, 0, true);
            Check(!state.Reliable, "late success cannot erase timeout/error");
        }
        var a = Completed("A", Operation.Buy); var b = Completed("B", Operation.Shrink, "CapacityBelowSold");
        foreach (bool created in new[] { false, true })
        foreach (bool verified in new[] { false, true })
            Check(EventWorkerState.CanCleanup(created, verified, new[] { "A", "B" }, new[] { a, b }) == (created && verified), "cleanup requires creation and verification");
        foreach (var key in new[] { "A", "B" })
        foreach (var status in new[] { "COMMIT_UNCERTAIN", "ROLLBACK_UNCERTAIN", "TECHNICAL_ERROR" })
        {
            var bad = new EventWorkerState(key, key == "A" ? Operation.Buy : Operation.Shrink);
            bad.Complete(Line(Packet(key, bad.Operation, status)), 2, true); bad.Stopped(false);
            Check(!EventWorkerState.CanCleanup(true, true, new[] { "A", "B" }, key == "A" ? new[] { bad, b } : new[] { a, bad }), "uncertain worker forbids cleanup despite both exits");
        }
        foreach (int worker in new[] { 0, 1 })
        foreach (bool forced in new[] { false, true })
        {
            var pair = new[] { Completed("A", Operation.Buy), Completed("B", Operation.Shrink, "CapacityBelowSold") };
            if (forced) pair[worker].Stopped(true); else pair[worker].StopUnconfirmed();
            Check(!EventWorkerState.CanCleanup(true, true, new[] { "A", "B" }, pair), "crash/unconfirmed exit forbids cleanup");
        }
        Check(!EventWorkerState.CanCleanup(true, true, new[] { "A", "B" }, new[] { a }), "missing worker");
        Check(!EventWorkerState.CanCleanup(true, true, new[] { "A", "B" }, new[] { a, a }), "duplicate worker");
        var cleanup = Completed("A", Operation.Buy, "CommittedWithCleanupFailure");
        Check(EventWorkerState.CanCleanup(true, true, new[] { "A", "B" }, new[] { cleanup, b }) && !cleanup.Strict, "confirmed cleanup failure allows scoped cleanup, not strict PASS");
        var replaced = new EventWorkerState("A", Operation.Buy); replaced.Complete(uncertain, 0, true);
        Check(!EventWorkerState.CanCleanup(true, true, new[] { "A", "B" }, new[] { replaced, b }), "mutation success -> uncertain forbids cleanup");
        foreach (int mask in new[] { 1, 2, 3 })
        {
            var pair = new[] { Completed("A", Operation.Buy), Completed("B", Operation.Shrink, "CapacityBelowSold") };
            for (int i = 0; i < 2; i++) if ((mask & (1 << i)) != 0) { pair[i].Fail(new TimeoutException()); pair[i].Stopped(false); }
            Check(!EventWorkerState.CanCleanup(true, true, new[] { "A", "B" }, pair), "one/both timed out even after exit");
        }
        foreach (string key in new[] { "S", "A", "B" })
        {
            var triple = new[] { Completed("S", Operation.Seed), Completed("A", Operation.Rename), Completed("B", Operation.Buy) };
            int index = Array.FindIndex(triple, w => w.Key == key);
            var bad = new EventWorkerState(key, triple[index].Operation);
            bad.Complete(Line(Packet(key, bad.Operation, "COMMIT_UNCERTAIN")), 2, true); triple[index] = bad;
            Check(!EventWorkerState.CanCleanup(true, true, new[] { "S", "A", "B" }, triple), "seed/admin/buyer unknown forbids stale-case cleanup");
        }
    }
    private static void FixturesAndMatrices()
    {
        Check(EventCase.All.Length == 8, "eight cases");
        foreach (var scenario in EventCase.All)
        {
            var plan = EventPlan.New(scenario);
            Check(EventPlan.Parse(plan.Marker, scenario.Name) == plan, "canonical GUID roundtrip");
            foreach (var invalid in new[] { "", "Nest-E52-x", plan.Marker.ToUpperInvariant(), plan.Marker + "x", "Nest-E52-" + Guid.NewGuid().ToString("D") })
                Reject(() => EventPlan.Parse(invalid, scenario.Name), "bad GUID marker");
            var row = new EventRow(123, plan.Title, plan.Description, plan.Owner, DateTime.Today.AddDays(30), TimeSpan.FromHours(19), "EventIntegrity", EventPlan.Price, scenario.Capacity, 0, "Aktiv", null, DateTime.Today);
            Check(EventFixture.Owned(row, plan, 123), "owned fixture");
            foreach (var bad in new[] { row with { Id = 124 }, row with { Owner = "foreign" }, row with { Description = "foreign" },
                row with { Title = "foreign" }, row with { Author = 1 }, row with { Sold = 11 }, row with { Price = 99 }, row with { Status = "unexpected" } })
                Check(!EventFixture.Owned(bad, plan, 123), "foreign/changed event rejected");
            Check(row.Request(Operation.Rename).Title == plan.Renamed && row.Request(Operation.Shrink).MaxTickets == 1, "public request capture");
            Check(plan.Guest("A").Length <= 80 && plan.Email("A").Length <= 80 && plan.Owner.Length <= 100, "fixture values fit guarded schema");
            var order = new OrderRow(9, 123, "TKT-20261118-ABCDEF012345", plan.Guest("A"), plan.Email("A"), EventPlan.Phone, 2, EventPlan.Price, 25m, null, DateTime.Now);
            Check(EventFixture.Owned(order, plan, 123), "own order");
            foreach (var bad in new[] { order with { Id = 0 }, order with { EventId = 124 }, order with { Name = "foreign" }, order with { Email = "foreign@example.invalid" },
                order with { Reservation = 1 }, order with { Quantity = 3 }, order with { Total = 99 }, order with { Code = "bad" } })
                Check(!EventFixture.Owned(bad, plan, 123), "unexpected order/dependency rejected");
            var variants = scenario.Kind switch
            {
                CaseKind.LastTickets => new[] { ("Success", "InsufficientTickets", 2, 2, "Aktiv"), ("InsufficientTickets", "Success", 2, 2, "Aktiv") },
                CaseKind.Capacity => new[] { ("Success", "CapacityBelowSold", 2, 4, "Aktiv"), ("InsufficientTickets", "Success", 0, 1, "Aktiv") },
                CaseKind.Status => new[] { ("Success", "Success", 2, 4, "Inaktiv"), ("EventNotActive", "Success", 0, 4, "Inaktiv") },
                _ => new[] { ("Success", "Success", 7, 10, "Aktiv") }
            };
            if (scenario.Kind == CaseKind.LastTickets)
            {
                var bothSuccess = new[] { Completed("A", Operation.Buy), Completed("B", Operation.Buy) };
                Check(!scenario.Matches(new EventSnapshot(row with { Sold = 2 }, Array.Empty<OrderRow>()), bothSuccess, plan), "double last-ticket success is rejected");
            }
            foreach (var v in variants)
            {
                var states = new List<EventWorkerState> { Completed("A", scenario.A, v.Item1), Completed("B", scenario.B, v.Item2) };
                if (scenario.Kind == CaseKind.Stale) states.Add(Completed("S", Operation.Seed));
                var orders = states.Where(w => w.Committed && EventWorkerState.Purchasing(w.Operation)).Select((w, i) =>
                    new OrderRow(i + 1, row.Id, w.Packet!.Code, plan.Guest(w.Key), plan.Email(w.Key), EventPlan.Phone,
                        w.Operation == Operation.Seed ? 5 : 2, w.Packet.Unit, w.Packet.Total, null, DateTime.Today)).ToArray();
                var snapshot = new EventSnapshot(row with { Title = scenario.Kind == CaseKind.Stale ? plan.Renamed : plan.Title, Sold = v.Item3, Max = v.Item4, Status = v.Item5 }, orders);
                bool expected = !scenario.Ordered || (v.Item1 == "Success") != scenario.AdminFirst;
                Check(scenario.Matches(snapshot, states, plan) == expected, "result/final matrix");
                Check(EventScenario.OrdersMatch(snapshot, states, plan), "orders tied to confirmed worker results");
                var unexpected = order with { Id = 999, Code = "TKT-20261118-FFFFFFFFFFFF" };
                Check(!EventScenario.OrdersMatch(snapshot with { Orders = orders.Append(unexpected).ToArray() }, states, plan), "unexpected dependency prevents cleanup");
                Check(!scenario.Matches(snapshot with { Row = snapshot.Row with { Sold = 99 } }, states, plan), "bad final state rejected");
                states[0].Fail(new TimeoutException());
                Check(!scenario.Matches(snapshot, states, plan), "timeout forbids verification");
            }
        }
    }
    private static void SchemaAndSource()
    {
        var columns = EventDatabaseGuard.RequiredColumns.ToDictionary(p => p.Key, p =>
            (Type: p.Value, Length: 200L, Nullable: true, Definition: p.Value == "enum" ? "enum('Aktiv','Inaktiv','Ausgebucht')" : p.Value == "decimal" ? "decimal(10,2)" : p.Value,
                Extra: p.Key.EndsWith(".id", StringComparison.Ordinal) ? "auto_increment" : ""));
        Check(EventDatabaseGuard.ColumnsSafe(columns), "expected schema");
        foreach (var key in columns.Keys.ToArray())
        {
            var missing = new Dictionary<string, (string, long, bool, string, string)>(columns); missing.Remove(key);
            Check(!EventDatabaseGuard.ColumnsSafe(missing), "required schema column");
            var changed = new Dictionary<string, (string, long, bool, string, string)>(columns) { [key] = ("unexpected", 200, true, "unexpected", "") };
            Check(!EventDatabaseGuard.ColumnsSafe(changed), "column type");
        }
        foreach (var key in new[] { "veranstaltungen.titel", "veranstaltungen.bild_url", "ticketbestellungen.gast_name", "ticketbestellungen.gast_email", "ticketbestellungen.gast_telefon", "ticketbestellungen.ticket_nummer" })
        {
            var changed = new Dictionary<string, (string Type, long Length, bool Nullable, string Definition, string Extra)>(columns);
            var item = changed[key]; changed[key] = item with { Length = 1 };
            Check(!EventDatabaseGuard.ColumnsSafe(changed), "schema width protects marker/code");
        }
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SafetyInputs", "EventRepository.source.txt"));
        var sql = Regex.Matches(source, "(?:const string|string)\\s+sql\\s*=\\s*@\"([\\s\\S]*?)\";").Cast<Match>()
            .Select(m => m.Groups[1].Value).Single(s => s.Contains("FOR UPDATE", StringComparison.Ordinal));
        var expanded = sql.Replace("@EventId", "123", StringComparison.Ordinal);
        Check(EventScenario.IsEventLock(expanded, 123), "recognizer matches CURRENT production query");
        Check(!EventScenario.IsEventLock(expanded, 124) && !EventScenario.IsEventLock(expanded.Replace("FOR UPDATE", ""), 123), "recognizer scope/lock");
        Check(!EventScenario.IsEventLock(expanded + " SELECT 1", 123), "recognizer rejects multiple SQL statements");
        Check(typeof(EventRepository).GetMethods().Where(m => m.Name == "Update").All(m => m.GetParameters().Single().ParameterType == typeof(EventUpdateRequest)), "current public API no old bypass");
        Check(typeof(EventUpdateRequest).GetProperty("SoldTickets") == null, "request excludes counter");
    }
}
