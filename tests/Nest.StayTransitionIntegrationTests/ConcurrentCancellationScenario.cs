using MySql.Data.MySqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

internal static class ConcurrentCancellationScenario
{
    internal static bool ObservationIsValid(IReadOnlyDictionary<B22Operation, int> sessions) => sessions.Count == 2 &&
        sessions.TryGetValue(B22Operation.CheckIn, out var checkIn) && sessions.TryGetValue(B22Operation.Cancel, out var cancel) &&
        checkIn > 0 && cancel > 0 && checkIn != cancel;

    internal static B22Operation? RoomLockOperation(string sql, int roomId)
    {
        if (roomId <= 0) return null;
        var suffix = @"\s+FROM\s+zimmer\s+WHERE\s+id\s*=\s*'?" + roomId.ToString(CultureInfo.InvariantCulture) + @"'?\s+FOR\s+UPDATE\s*;?\s*$";
        if (Regex.IsMatch(sql, @"^\s*SELECT\s+id\s*,\s*status\s*,\s*geloescht_am" + suffix, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return B22Operation.CheckIn;
        if (Regex.IsMatch(sql, @"^\s*SELECT\s+id" + suffix, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return B22Operation.Cancel;
        return null;
    }

    internal static async Task Run()
    {
        var fixture = new B22FixtureSet(B22FixturePlan.Create());
        Console.WriteLine("B22 RUN " + fixture.Plan.Marker);
        var workers = new List<Worker>();
        var verified = false;
        Exception? failure = null;
        try
        {
            fixture.Create();
            Console.WriteLine($"B22 FIXTURES room={fixture.RoomId}; guest={fixture.GuestId}; booking={fixture.BookingId}; code={fixture.Plan.Code}");
            using (var gateConnection = B22DatabaseGuard.Open())
            using (var gate = gateConnection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
            {
                try
                {
                    var initial = fixture.ReadOwnedRows(gateConnection, gate);
                    FixtureSet.Require(initial.Booking == "Reserviert" && initial.Room == "Verfuegbar" && !initial.Deleted);
                    using var observer = B22DatabaseGuard.Open();
                    workers.Add(Worker.Start(fixture, "A", B22Operation.CheckIn));
                    workers.Add(Worker.Start(fixture, "B", B22Operation.Cancel));
                    await Task.WhenAll(workers.Select(worker => worker.Ready()));
                    await Task.WhenAll(workers.Select(worker => worker.Go()));
                    var sessions = await Observe(observer, fixture.RoomId, gateConnection.ServerThread);
                    Console.WriteLine($"B22 EVIDENCE room FOR UPDATE: CheckIn session={sessions[B22Operation.CheckIn]}; Cancel session={sessions[B22Operation.Cancel]}; coordinator still holds its room lock.");
                    Console.WriteLine("B22 gate/query evidence is NOT a direct InnoDB lock-wait graph assertion; neither winner is presumed.");
                }
                finally
                {
                    gate.Rollback();
                    Console.WriteLine("B22 GATE released by coordinator Rollback; no coordinator data change committed.");
                }
            }
            await Task.WhenAll(workers.Select(worker => worker.Finish()));
            var states = workers.Select(worker => worker.State).ToArray();
            foreach (var worker in workers) worker.State.Report();
            FixtureSet.Require(states.All(state => state.HasReliableResult));
            using var verification = B22DatabaseGuard.Open();
            var actual = fixture.ReadOwnedRows(verification);
            FixtureSet.Require(B22Safety.StateIsConsistent(states, actual.Booking, actual.Room, actual.Deleted));
            verified = true;
            Console.WriteLine($"B22 FINAL booking={actual.Booking}; room={actual.Room}; deleted={actual.Deleted}");
            // A known Commit with resource-cleanup failure may permit safe fixture cleanup,
            // but must not satisfy the strict successful-operation matrix.
            FixtureSet.Require(B22Safety.IsPass(states, actual.Booking, actual.Room, actual.Deleted));
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            foreach (var worker in workers)
                try { await worker.StopAndWait(); }
                catch (Exception ex) { worker.State.StopUnconfirmed(); failure ??= ex; }
            var states = workers.Select(worker => worker.State).ToArray();
            if (B22Safety.CanCleanup(fixture.Confirmed, verified, states))
            {
                try { fixture.Cleanup(states, verified); }
                catch (Exception ex) { failure ??= ex; fixture.ReportRetained(); }
            }
            else
            {
                failure ??= new InvalidOperationException("B2.2 setup, outcomes, exits or final state unconfirmed.");
                fixture.ReportRetained();
            }
            foreach (var worker in workers)
            {
                worker.State.Report();
                try { worker.Dispose(); } catch (Exception ex) { failure ??= ex; }
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS B2.2: one committed winner, expected business refusal, consistent final state and scoped cleanup.");
    }

    private static async Task<Dictionary<B22Operation, int>> Observe(MySqlConnection observer, int roomId, int gateThread)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            using var command = TestDatabaseGuard.Command("SELECT ID, INFO FROM information_schema.PROCESSLIST WHERE USER = @user AND DB = DATABASE() AND ID <> CONNECTION_ID() AND ID <> @gate", observer);
            command.Parameters.AddWithValue("@user", "nest_test_runner"); command.Parameters.AddWithValue("@gate", gateThread);
            var sessions = new Dictionary<B22Operation, int>();
            using (var rows = command.ExecuteReader())
                while (rows.Read())
                {
                    var operation = rows.IsDBNull(1) ? null : RoomLockOperation(rows.GetString(1), roomId);
                    if (operation is { } kind)
                        FixtureSet.Require(sessions.TryAdd(kind, rows.GetInt32(0)));
                }
            if (sessions.Count == 2)
            {
                FixtureSet.Require(ObservationIsValid(sessions));
                return sessions;
            }
            await Task.Delay(50, deadline.Token); // Observe a condition; no Thread.Sleep or presumed FIFO order.
        }
    }

    private sealed class Worker(Process process, B22WorkerState state) : IDisposable
    {
        internal B22WorkerState State { get; } = state;
        internal static Worker Start(B22FixtureSet fixture, string id, B22Operation operation)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "--execute-b22", "--worker", fixture.Plan.Marker,
                fixture.Plan.Code, fixture.RoomId.ToString(CultureInfo.InvariantCulture), id, operation.ToString() })
                start.ArgumentList.Add(argument); // Only synthetic fixture IDs and operation, never credentials.
            var child = Process.Start(start) ?? throw new InvalidOperationException("B2.2 worker not started.");
            return new Worker(child, new B22WorkerState(id, operation, fixture.Plan.Code));
        }
        internal async Task Ready()
        {
            try
            {
                process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine();
                FixtureSet.Require(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) == $"READY {State.Id} {State.Operation}");
            }
            catch (Exception ex) { State.ObserveFailure(ex); throw; }
        }
        internal async Task Go()
        {
            try { await process.StandardInput.WriteLineAsync("GO"); await process.StandardInput.FlushAsync(); }
            catch (Exception ex) { State.ObserveFailure(ex); throw; }
        }
        internal Task Finish() => State.ReadCompletion(
            async () => await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45)),
            () => process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)), () => process.ExitCode);
        internal async Task StopAndWait()
        {
            try
            {
                var forced = !process.HasExited;
                if (forced) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                FixtureSet.Require(process.HasExited); State.ConfirmStopped(forced);
            }
            catch (Exception ex) { State.ObserveFailure(ex); State.StopUnconfirmed(); throw; }
        }
        public void Dispose() => process.Dispose();
    }
}
