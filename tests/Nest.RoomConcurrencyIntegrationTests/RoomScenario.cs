using MySql.Data.MySqlClient;
using Nest.Data;
using Nest.Models;
using System.Data;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

internal enum RoomOperation { Update, Delete, NoOp }
internal sealed record RoomCase(string Name, RoomOperation First, RoomOperation? Second, bool Ordered)
{
    internal static readonly RoomCase[] All = {
        new("concurrent-update-update", RoomOperation.Update, RoomOperation.Update, false),
        new("concurrent-update-delete", RoomOperation.Update, RoomOperation.Delete, false),
        new("concurrent-delete-delete", RoomOperation.Delete, RoomOperation.Delete, false),
        new("ordered-update-before-delete", RoomOperation.Update, RoomOperation.Delete, true),
        new("ordered-delete-before-update", RoomOperation.Delete, RoomOperation.Update, true),
        new("unchanged-found-row", RoomOperation.NoOp, null, false)
    };
    internal int Count => Second == null ? 1 : 2;
    internal static RoomOperation ParseOperation(string value)
    {
        foreach (var operation in Enum.GetValues<RoomOperation>()) if (value == operation.ToString()) return operation;
        throw new InvalidOperationException();
    }
    internal static Room Proposed(RoomOriginalValues baseline, RoomPlan plan, string key, RoomOperation operation) => new() {
        Id = baseline.Id, RoomNumber = int.Parse(baseline.RoomNumber, CultureInfo.InvariantCulture), Type = baseline.Type,
        Capacity = baseline.Capacity, PricePerNight = operation == RoomOperation.NoOp ? baseline.PricePerNight : baseline.PricePerNight + (key == "A" ? 1 : 2),
        Status = baseline.Status, Description = operation == RoomOperation.NoOp ? baseline.Description : plan.Description(key),
        ImagePath = baseline.ImagePath, DateAdded = baseline.DateAdded ?? default, DateDelete = baseline.DateDelete
    };
    private static RoomWriteResult Normalize(RoomWriteResult value) => value == RoomWriteResult.CommittedWithCleanupFailure ? RoomWriteResult.Success : value;
    internal bool Matrix(IReadOnlyList<RoomWriteResult> values, bool strict)
    {
        if (values.Count != Count || strict && values.Contains(RoomWriteResult.CommittedWithCleanupFailure)) return false;
        var a = Normalize(values[0]); var b = Count == 2 ? Normalize(values[1]) : (RoomWriteResult?)null;
        return Name switch {
            "concurrent-update-update" => a == RoomWriteResult.Success && b == RoomWriteResult.ConcurrencyConflict || a == RoomWriteResult.ConcurrencyConflict && b == RoomWriteResult.Success,
            "concurrent-update-delete" => a == RoomWriteResult.Success && b == RoomWriteResult.ConcurrencyConflict || a == RoomWriteResult.Missing && b == RoomWriteResult.Success,
            "concurrent-delete-delete" => a == RoomWriteResult.Success && b == RoomWriteResult.Missing || a == RoomWriteResult.Missing && b == RoomWriteResult.Success,
            "ordered-update-before-delete" => a == RoomWriteResult.Success && b == RoomWriteResult.ConcurrencyConflict,
            "ordered-delete-before-update" => a == RoomWriteResult.Success && b == RoomWriteResult.Missing,
            "unchanged-found-row" => a == RoomWriteResult.Success,
            _ => false
        };
    }
    internal bool FinalMatches(RoomOriginalValues baseline, RoomOriginalValues current, RoomPlan plan, IReadOnlyList<RoomWriteResult> results)
    {
        if (!Matrix(results, strict: false)) return false;
        var winner = Normalize(results[0]) == RoomWriteResult.Success ? 0 : 1;
        var operation = winner == 0 ? First : Second!.Value;
        if (operation == RoomOperation.Delete) return current.DateDelete != null && current == baseline with { DateDelete = current.DateDelete };
        if (operation == RoomOperation.NoOp) return current == baseline;
        var proposed = Proposed(baseline, plan, winner == 0 ? "A" : "B", operation);
        return current == baseline with { PricePerNight = proposed.PricePerNight, Description = proposed.Description };
    }
}

internal static class RoomScenario
{
    internal static bool IsRoomLockStatement(string sql, int id) =>
        sql.Contains("hinzugefuegt_am", StringComparison.OrdinalIgnoreCase) && sql.Contains("zimmer_nummer", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(sql, @"^\s*SELECT\s+id\s*,[\s\S]*\bFROM\s+zimmer\s+WHERE\s+id\s*=\s*'?" + id.ToString(CultureInfo.InvariantCulture) + @"'?\s+FOR\s+UPDATE\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
        !sql.Trim().TrimEnd(';').Contains(';');

    internal static async Task RunSuite()
    {
        using (var preflight = RoomDatabaseGuard.Open()) RoomDatabaseGuard.Preflight(preflight);
        Console.WriteLine("TARGET verified: nest_integration_test; nest_test_runner@localhost; localhost:3306; UseAffectedRows=false; isolated SELECT/INSERT/UPDATE/DELETE grants.");
        foreach (var scenario in RoomCase.All) await Run(scenario); // Failure stops the suite; never retry a scenario.
        Console.WriteLine("PASS: all six room scenarios, full final-state checks and scoped cleanup confirmed.");
    }
    private static async Task Run(RoomCase scenario)
    {
        var fixture = new RoomFixture(RoomPlan.New());
        var workers = new List<RoomWorker>();
        var observedSessions = new HashSet<int>();
        RoomOriginalValues? verified = null;
        bool finalVerified = false;
        Exception? failure = null;
        Console.WriteLine($"RUN {scenario.Name}; marker={fixture.Plan.Marker}; number={fixture.Plan.Number}");
        try
        {
            fixture.Create();
            Console.WriteLine($"FIXTURE room={fixture.Id}; marker={fixture.Plan.Marker}");
            var baseline = fixture.Initial ?? throw new InvalidOperationException();
            workers.Add(RoomWorker.Start(fixture, scenario.First, "A"));
            if (scenario.Second is { } second) workers.Add(RoomWorker.Start(fixture, second, "B"));
            await Task.WhenAll(workers.Select(w => w.Ready(Safety.Fingerprint(baseline))));
            using var observer = RoomDatabaseGuard.Open();
            if (scenario.Ordered)
            {
                // This deliberately controls API completion order, NOT InnoDB queue order.
                // Both independent workers already hold the SAME old baseline.
                observedSessions.UnionWith(await ReleaseRound(fixture, observer, new[] { workers[0] }));
                await workers[0].Finish();
                Safety.Require(workers[0].State.Committed);
                observedSessions.UnionWith(await ReleaseRound(fixture, observer, new[] { workers[1] }));
                await workers[1].Finish();
            }
            else
            {
                observedSessions.UnionWith(await ReleaseRound(fixture, observer, workers));
                await Task.WhenAll(workers.Select(w => w.Finish()));
            }
            Safety.Require(observedSessions.Count == scenario.Count && workers.All(w => w.State.Reliable));
            foreach (var worker in workers) worker.Report();
            var results = workers.Select(w => w.State.Result!.Value).ToArray();
            Safety.Require(scenario.Matrix(results, strict: false));
            await WaitSessionsGone(observer, observedSessions);
            using (var connection = RoomDatabaseGuard.Open())
            using (var probe = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    verified = RoomFixture.ReadOwned(connection, fixture.Plan, fixture.Id, probe);
                    RoomFixture.AssertNoDependencies(connection, fixture.Id, probe);
                    Safety.Require(scenario.FinalMatches(baseline, verified, fixture.Plan, results));
                }
                finally { probe.Rollback(); }
            }
            finalVerified = true;
            Console.WriteLine($"FINAL room={fixture.Id}; status={verified.Status}; deleted={verified.DateDelete != null}; price={verified.PricePerNight.ToString(CultureInfo.InvariantCulture)}; description={verified.Description}; worker sessions gone and lock probe released.");
            // Confirmed Commit + cleanup failure may allow safe fixture cleanup, but never strict PASS.
            if (!scenario.Matrix(results, strict: true)) failure = new InvalidOperationException();
        }
        catch (Exception ex) { failure ??= ex; }
        finally
        {
            foreach (var worker in workers)
            {
                try { await worker.StopAndWait(); }
                catch (Exception ex) { worker.State.StopUnconfirmed(); failure ??= ex; }
            }
            var states = workers.Select(w => w.State).ToArray();
            if (verified != null && RoomWorkerState.CanCleanup(fixture.CreatedConfirmed, finalVerified, states, scenario.Count))
            {
                try { fixture.Cleanup(verified, states, finalVerified, scenario.Count); }
                catch (Exception ex) { failure ??= ex; fixture.ReportRetained(); }
            }
            else { failure ??= new InvalidOperationException(); fixture.ReportRetained(); }
            foreach (var worker in workers)
            {
                worker.Report();
                try { worker.Dispose(); } catch (Exception ex) { failure ??= ex; }
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS " + scenario.Name);
    }
    private static async Task<int[]> ReleaseRound(RoomFixture fixture, MySqlConnection observer, IReadOnlyList<RoomWorker> workers)
    {
        using var connection = RoomDatabaseGuard.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var observed = false;
        try
        {
            RoomFixture.ReadOwned(connection, fixture.Plan, fixture.Id, transaction);
            RoomFixture.AssertNoDependencies(connection, fixture.Id, transaction);
            await Task.WhenAll(workers.Select(w => w.Go()));
            var ids = await ObserveLocks(observer, fixture.Id, connection.ServerThread, workers.Count);
            observed = true;
            Console.WriteLine($"OBSERVED room={fixture.Id}; independent production FOR UPDATE sessions={string.Join(',', ids)}; coordinator still holds its row lock. PROCESSLIST is NOT an InnoDB wait graph or FIFO proof.");
            return ids;
        }
        finally
        {
            transaction.Rollback();
            Console.WriteLine($"RELEASED coordinator room={fixture.Id}; required queries observed={observed}. No coordinator data changes.");
        }
    }
    private static async Task<int[]> ObserveLocks(MySqlConnection observer, int roomId, int gate, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            using var command = RoomDatabaseGuard.Command("SELECT ID, INFO FROM information_schema.PROCESSLIST WHERE USER = @user AND DB = DATABASE() AND ID <> CONNECTION_ID() AND ID <> @gate", observer);
            command.Parameters.AddWithValue("@user", "nest_test_runner"); command.Parameters.AddWithValue("@gate", gate);
            var ids = new HashSet<int>();
            using (var reader = command.ExecuteReader())
                while (reader.Read()) if (!reader.IsDBNull(1) && IsRoomLockStatement(reader.GetString(1), roomId)) ids.Add(reader.GetInt32(0));
            if (ids.Count == count) return ids.Order().ToArray();
            Safety.Require(ids.Count < count);
            await Task.Delay(40, deadline.Token); // Poll an observable condition, never assume lock acquisition from elapsed time.
        }
    }
    private static async Task WaitSessionsGone(MySqlConnection observer, IReadOnlyCollection<int> ids)
    {
        Safety.Require(ids.Count is 1 or 2);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            using var command = RoomDatabaseGuard.Command("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE USER = @user AND ID IN (@a, @b)", observer);
            command.Parameters.AddWithValue("@user", "nest_test_runner");
            command.Parameters.AddWithValue("@a", ids.First()); command.Parameters.AddWithValue("@b", ids.Last());
            if (Convert.ToInt32(command.ExecuteScalar()) == 0) return;
            await Task.Delay(40, deadline.Token);
        }
    }
}
