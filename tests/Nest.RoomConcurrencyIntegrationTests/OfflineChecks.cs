using MySql.Data.MySqlClient;
using Nest.Data;
using Nest.Models;
using System.Globalization;
using System.Text.RegularExpressions;

internal static class OfflineChecks
{
    private static int assertions;
    private static void Check(bool value)
    { if (!value) throw new InvalidOperationException("Room integration offline invariant failed."); assertions++; }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; } catch (FormatException) { rejected = true; } catch (OverflowException) { rejected = true; }
        Check(rejected);
    }
    internal static Task Run()
    {
        var keys = new[] { IntegrationConnectionSettings.ModeVariable, IntegrationConnectionSettings.ConnectionVariable, RoomDatabaseGuard.WriteVariable };
        var old = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
        try
        {
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, null);
            Admission(); Console.WriteLine("PASS room offline admission/configuration");
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, null);
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, "offline-only-invalid");
            Matrices(); Console.WriteLine("PASS room offline result/final-state matrix");
            Workers(); Console.WriteLine("PASS room offline worker/cleanup safety");
            PlansAndSql(); Console.WriteLine("PASS room offline fixture plans");
            SourceChecks(); Console.WriteLine("PASS room offline current source/SQL shape checks");
        }
        finally
        {
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, old[key] as string);
        }
        Console.WriteLine($"PASS: {assertions} room integration offline assertions (admission, outcomes, final states, worker protocol, cleanup safety, fixture/source invariants). No credentials read, connections opened or SQL executed.");
        return Task.CompletedTask;
    }
    private static void Admission()
    {
        var flag = Program.ExecuteFlag;
        Check(Program.IsController(new[] { flag }));
        Check(!Program.IsController(Array.Empty<string>()));
        foreach (var args in new[] { new[] { "--execute-b21" }, new[] { "--execute-b22" }, new[] { flag, "extra" }, new[] { "--worker" } })
            Check(!Program.IsController(args) && !Program.IsWorker(args));
        Check(Program.IsWorker(new[] { flag, "--worker", "marker", "1", "900000000", "Update", "A" }));
        foreach (var mode in new string?[] { null, "", "0", "true", "wrong", "1" })
        foreach (var allow in new string?[] { null, "", "hotel", "1", "nest_integration_test" })
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, mode);
            Environment.SetEnvironmentVariable(RoomDatabaseGuard.WriteVariable, allow);
            if (mode == "1" && allow == "nest_integration_test") RoomDatabaseGuard.RequireAuthorization();
            else { Reject(RoomDatabaseGuard.RequireAuthorization); Reject(RoomDatabaseGuard.ConfigureRunner); }
        }
        Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, "1");
        Environment.SetEnvironmentVariable(RoomDatabaseGuard.WriteVariable, "nest_integration_test");
        Reject(RoomDatabaseGuard.RequireRunner); // Missing connection: no secret retrieval in this method.
        var valid = new MySqlConnectionStringBuilder { Server = "localhost", Port = 3306,
            Database = "nest_integration_test", UserID = "nest_test_runner", Password = "non-secret-offline-placeholder", UseAffectedRows = false };
        Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, valid.ConnectionString);
        RoomDatabaseGuard.RequireRunner(); Check(!new MySqlConnectionStringBuilder(IntegrationConnectionSettings.Parse(valid.ConnectionString).ConnectionString).UseAffectedRows);
        foreach (var pair in new (string Key, object Value)[] { ("Server", "127.0.0.1"), ("Server", "remote"), ("Port", 3307),
            ("Database", "hotel"), ("Database", "other"), ("User ID", "root"), ("User ID", "nest_test_schema"),
            ("User ID", "nest_test_app"), ("Password", ""), ("UseAffectedRows", true), ("Pooling", true) })
        {
            var wrong = new MySqlConnectionStringBuilder(valid.ConnectionString) { [pair.Key] = pair.Value };
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ConnectionVariable, wrong.ConnectionString);
            Reject(RoomDatabaseGuard.RequireRunner);
        }
        const string usage = "GRANT USAGE ON *.* TO 'nest_test_runner'@'localhost'";
        const string isolated = "GRANT SELECT, INSERT, UPDATE, DELETE ON `nest_integration_test`.* TO 'nest_test_runner'@'localhost'";
        Check(RoomDatabaseGuard.GrantsAreIsolated(new[] { usage, isolated }));
        foreach (var extra in new[] { "GRANT SELECT ON `hotel`.* TO 'nest_test_runner'@'localhost'", "GRANT ALL PRIVILEGES ON *.* TO x",
            isolated + " WITH GRANT OPTION", "GRANT PROCESS ON *.* TO x", "GRANT CREATE ON `nest_integration_test`.* TO x" })
            Check(!RoomDatabaseGuard.GrantsAreIsolated(new[] { usage, isolated, extra }));
        Check(!RoomDatabaseGuard.GrantsAreIsolated(new[] { usage }));
    }
    private static RoomOriginalValues Initial(RoomPlan plan) => new(42, plan.Number.ToString(CultureInfo.InvariantCulture), "Standard", 1, 50.25m,
        "Verfuegbar", plan.Description("original"), plan.Owner, new DateTime(2026, 11, 11), null);
    private static void Matrices()
    {
        var plan = RoomPlan.New(); var baseline = Initial(plan);
        foreach (var scenario in RoomCase.All)
        foreach (var a in Enum.GetValues<RoomWriteResult>())
        foreach (var b in scenario.Count == 1 ? new[] { RoomWriteResult.Success } : Enum.GetValues<RoomWriteResult>())
        {
            var values = scenario.Count == 1 ? new[] { a } : new[] { a, b };
            // Independent expected matrix from the specification; not a copy of Repository SQL.
            var expected = scenario.Name switch {
                "concurrent-update-update" => a == RoomWriteResult.Success && b == RoomWriteResult.ConcurrencyConflict || b == RoomWriteResult.Success && a == RoomWriteResult.ConcurrencyConflict,
                "concurrent-update-delete" => a == RoomWriteResult.Success && b == RoomWriteResult.ConcurrencyConflict || b == RoomWriteResult.Success && a == RoomWriteResult.Missing,
                "concurrent-delete-delete" => a == RoomWriteResult.Success && b == RoomWriteResult.Missing || b == RoomWriteResult.Success && a == RoomWriteResult.Missing,
                "ordered-update-before-delete" => a == RoomWriteResult.Success && b == RoomWriteResult.ConcurrencyConflict,
                "ordered-delete-before-update" => a == RoomWriteResult.Success && b == RoomWriteResult.Missing,
                "unchanged-found-row" => a == RoomWriteResult.Success,
                _ => false
            };
            Check(scenario.Matrix(values, strict: true) == expected);
            if (!expected) continue;
            var index = a == RoomWriteResult.Success ? 0 : 1;
            var operation = index == 0 ? scenario.First : scenario.Second!.Value;
            var proposed = RoomCase.Proposed(baseline, plan, index == 0 ? "A" : "B", operation);
            var final = operation == RoomOperation.Delete ? baseline with { DateDelete = DateTime.Today } :
                operation == RoomOperation.NoOp ? baseline : baseline with { PricePerNight = proposed.PricePerNight, Description = proposed.Description };
            Check(scenario.FinalMatches(baseline, final, plan, values));
            foreach (var damaged in new[] { final with { Capacity = 2 }, final with { ImagePath = "wrong" }, final with { RoomNumber = "other" },
                final with { PricePerNight = final.PricePerNight + .01m }, final with { Description = "lost-marker" }, final with { Status = "Belegt" },
                final with { Type = "Suite" }, final with { DateAdded = final.DateAdded!.Value.AddSeconds(1) } })
                Check(!scenario.FinalMatches(baseline, damaged, plan, values));
            var cleanupFailure = values.Select(v => v == RoomWriteResult.Success ? RoomWriteResult.CommittedWithCleanupFailure : v).ToArray();
            Check(!scenario.Matrix(cleanupFailure, strict: true) && scenario.Matrix(cleanupFailure, strict: false));
        }
        foreach (var scenario in RoomCase.All.Where(c => c.Count == 2)) Check(!scenario.Matrix(new[] { RoomWriteResult.Success, RoomWriteResult.Success }, strict: true));
    }
    private static RoomWorkerState Completed(string key, string text, int code = 0, bool stopped = true)
    { var state = new RoomWorkerState(key); state.Complete(text, code, stopped); return state; }
    private static void Workers()
    {
        foreach (var result in Enum.GetValues<RoomWriteResult>())
        foreach (var stopped in new[] { false, true })
        foreach (var exit in new[] { 0, 2, -1 })
        {
            var state = Completed("A", "RESULT " + result + "\n", exit, stopped);
            var reliable = stopped && exit == 0 && result is not (RoomWriteResult.CommitUncertain or RoomWriteResult.UncertainOutcome or RoomWriteResult.TechnicalFailure);
            Check(state.Reliable == reliable);
            Check(!RoomWorkerState.CanCleanup(true, false, new[] { state }, 1));
            Check(!RoomWorkerState.CanCleanup(false, true, new[] { state }, 1));
            Check(RoomWorkerState.CanCleanup(true, true, new[] { state }, 1) == reliable);
        }
        var success = Completed("A", "RESULT Success\n");
        var refusal = Completed("B", "RESULT ConcurrencyConflict\n");
        Check(RoomWorkerState.CanCleanup(true, true, new[] { success, refusal }, 2));
        Check(!RoomWorkerState.CanCleanup(true, true, new[] { success }, 2));
        Check(!RoomWorkerState.CanCleanup(true, true, new[] { success, success }, 2));
        foreach (var text in new[] { "", "RESULT", "RESULT 0\n", "RESULT Success \n", "RESULT Unknown\n", "RESULT TECHNICAL_ERROR\n",
            "RESULT Success\nRESULT Success\n", "RESULT Success\nRESULT CommitUncertain\n", "RESULT UncertainOutcome\n" })
        foreach (var code in new[] { 0, 2, -1 })
        foreach (var side in new[] { 0, 1 })
        {
            var bad = Completed(side == 0 ? "A" : "B", text, code);
            var states = side == 0 ? new[] { bad, refusal } : new[] { success, bad };
            Check(!RoomWorkerState.CanCleanup(true, true, states, 2));
            bad.ConfirmStopped(false);
            Check(!RoomWorkerState.CanCleanup(true, true, states, 2)); // Both exited is NOT enough.
        }
        foreach (var side in new[] { 0, 1 })
        {
            var uncertain = Completed(side == 0 ? "A" : "B", "RESULT CommitUncertain\n", 2);
            uncertain.Fail(new TimeoutException()); uncertain.ConfirmStopped(true);
            Check(uncertain.Outcome == RoomOutcome.CommitUncertain);
            Check(!RoomWorkerState.CanCleanup(true, true, side == 0 ? new[] { uncertain, refusal } : new[] { success, uncertain }, 2));
        }
        foreach (var error in new Exception[] { new TimeoutException(), new InvalidOperationException() })
        foreach (var side in new[] { 0, 1 })
        {
            var failed = new RoomWorkerState(side == 0 ? "A" : "B"); failed.Fail(error); failed.ConfirmStopped(false);
            Check(!RoomWorkerState.CanCleanup(true, true, side == 0 ? new[] { failed, refusal } : new[] { success, failed }, 2));
        }
        var bothTimedOut = new[] { new RoomWorkerState("A"), new RoomWorkerState("B") };
        foreach (var w in bothTimedOut) { w.Fail(new TimeoutException()); w.ConfirmStopped(true); }
        Check(!RoomWorkerState.CanCleanup(true, true, bothTimedOut, 2));
        var killed = Completed("A", "RESULT Success\n"); killed.ConfirmStopped(true);
        Check(!killed.Reliable);
        var stopUnknown = Completed("A", "RESULT Success\n"); stopUnknown.StopUnconfirmed();
        Check(!RoomWorkerState.CanCleanup(true, true, new[] { stopUnknown, refusal }, 2));
        var cleanup = Completed("A", "RESULT CommittedWithCleanupFailure\n");
        Check(cleanup.Committed && cleanup.Reliable && cleanup.Outcome != RoomOutcome.CommitUncertain);
        Check(RoomWorkerState.CanCleanup(true, true, new[] { cleanup, refusal }, 2));
        // Mutation check: replacing confirmed Success by uncertain Commit forbids cleanup.
        var mutation = Completed("A", "RESULT CommitUncertain\n", 2);
        Check(!RoomWorkerState.CanCleanup(true, true, new[] { mutation, refusal }, 2));
        var afterCommitFailure = Completed("A", "RESULT Success\n");
        afterCommitFailure.Fail(new System.IO.IOException());
        Check(!RoomWorkerState.CanCleanup(true, true, new[] { afterCommitFailure, refusal }, 2));
        var noResult = Completed("A", "", 0, stopped: false);
        Reject(() => noResult.Complete("RESULT Success\n", 0, true));
        Check(!RoomWorkerState.CanCleanup(true, true, new[] { noResult, refusal }, 2));
    }
    private static void PlansAndSql()
    {
        var plans = Enumerable.Range(0, 50).Select(_ => RoomPlan.New()).ToArray();
        Check(plans.Select(p => p.Marker).Distinct().Count() == plans.Length);
        foreach (var plan in plans)
        {
            Check(RoomPlan.Parse(plan.Marker, plan.Number.ToString(CultureInfo.InvariantCulture)) == plan);
            Check(plan.Number.ToString(CultureInfo.InvariantCulture).Length <= 10 && plan.Owner.Length <= 500);
            var row = Initial(plan);
            Check(RoomFixture.IsOwned(row, plan, 42));
            foreach (var other in new[] { row with { Id = 43 }, row with { ImagePath = row.ImagePath!.ToUpperInvariant() },
                row with { Description = "other" }, row with { RoomNumber = "0" + row.RoomNumber } })
                Check(!RoomFixture.IsOwned(other, plan, 42));
        }
        foreach (var invalid in new[] { "wrong", "Nest-RoomCC-", plans[0].Marker.ToUpperInvariant() }) Reject(() => RoomPlan.Parse(invalid, "900000000"));
        foreach (var invalid in new[] { "0", "9000000000", "0900000000", "hotel" }) Reject(() => RoomPlan.Parse(plans[0].Marker, invalid));
        foreach (var invalid in new[] { "0", "UPDATE", "CheckIn", "Delete,Update" }) Reject(() => RoomCase.ParseOperation(invalid));
        Check(RoomFixture.InsertSql.StartsWith("INSERT INTO zimmer ", StringComparison.Ordinal));
        Check(RoomFixture.DeleteSql.Contains("WHERE id = @id") && RoomFixture.DeleteSql.Contains("BINARY bild_pfad = BINARY @owner") &&
            RoomFixture.DeleteSql.Contains("BINARY beschreibung = BINARY @description"));
    }
    private static string Source(string name) => System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "SafetyInputs", name + ".source.txt"));
    private static void SourceChecks()
    {
        var repository = Source("RoomRepository");
        var start = repository.IndexOf("private static RoomOriginalValues? LockRoom(", StringComparison.Ordinal);
        var end = repository.IndexOf("private static RoomWriteResult ReadReservationProtection(", StringComparison.Ordinal);
        Check(start >= 0 && end > start);
        var sql = Regex.Match(repository[start..end], "@\"([\\s\\S]*?)\"").Groups[1].Value;
        Check(sql.Contains("@Id", StringComparison.Ordinal));
        var expanded = sql.Replace("@Id", "42", StringComparison.Ordinal);
        Check(RoomScenario.IsRoomLockStatement(expanded, 42));
        Check(!RoomScenario.IsRoomLockStatement(expanded, 4));
        Check(!RoomScenario.IsRoomLockStatement(expanded.Replace("FOR UPDATE", "", StringComparison.Ordinal), 42));
        Check(!RoomScenario.IsRoomLockStatement(expanded + " SELECT 1", 42));
        Check(!RoomScenario.IsRoomLockStatement(expanded.Replace("zimmer", "reservierungen", StringComparison.Ordinal), 42));
        var scenario = Source("RoomScenario");
        Check(scenario.IndexOf("RoomDatabaseGuard.Preflight(preflight)", StringComparison.Ordinal) < scenario.IndexOf("foreach (var scenario in RoomCase.All)", StringComparison.Ordinal));
        Check(scenario.IndexOf("await worker.StopAndWait()", StringComparison.Ordinal) < scenario.IndexOf("fixture.Cleanup(", StringComparison.Ordinal));
        Check(scenario.Contains("await WaitSessionsGone(observer, observedSessions)") && scenario.Contains("finalVerified = true"));
        var fixture = Source("RoomFixture");
        Check(fixture.IndexOf("current == verified", StringComparison.Ordinal) < fixture.IndexOf("Command(DeleteSql", StringComparison.Ordinal));
        Check(fixture.Contains("AssertNoDependencies(connection, Id, transaction)") && fixture.Contains("SELECT COUNT(*) FROM zimmer WHERE id = @id OR bild_pfad = @owner"));
        var guard = Source("RoomDatabaseGuard");
        var write = guard[guard.IndexOf("internal static int WriteOne(", StringComparison.Ordinal)..];
        Check(write.IndexOf("AssertTarget(", StringComparison.Ordinal) < write.IndexOf("ExecuteNonQuery()", StringComparison.Ordinal));
        var program = Source("Program");
        Check(program.Contains("repository.Delete(expected)") && program.Contains("repository.Update(RoomCase.Proposed(expected"));
        Check(program.IndexOf("ReadLineAsync().WaitAsync", StringComparison.Ordinal) < program.IndexOf("repository.Delete(expected)", StringComparison.Ordinal));
        foreach (var source in new[] { fixture, scenario, guard, program, Source("RoomWorker") })
        {
            Check(!Regex.IsMatch(source, @"\b(?:TRUNCATE|DROP\s+TABLE|ALTER\s+TABLE|FOREIGN_KEY_CHECKS)\b", RegexOptions.IgnoreCase));
            Check(!source.Contains("hotel.", StringComparison.OrdinalIgnoreCase) && !source.Contains("Thread.Sleep", StringComparison.Ordinal));
        }
        // No business UPDATE/DELETE SQL is copied into fixture/controller/worker code.
        Check(!Regex.IsMatch(fixture + scenario + program + Source("RoomWorker"), @"UPDATE\s+zimmer\s+(?:SET|z\b)", RegexOptions.IgnoreCase));
    }
}
