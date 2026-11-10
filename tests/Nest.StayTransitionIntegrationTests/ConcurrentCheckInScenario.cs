using MySql.Data.MySqlClient;
using Nest.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

internal static class ConcurrentCheckInScenario
{
    internal static bool ResultsAreCorrect(StayTransitionResult first, StayTransitionResult second) =>
        first == StayTransitionResult.Success && second == StayTransitionResult.OccupancyConflict ||
        second == StayTransitionResult.Success && first == StayTransitionResult.OccupancyConflict;

    internal static bool IsRoomLockStatement(string sql, int roomId) =>
        sql.Contains("geloescht_am", StringComparison.OrdinalIgnoreCase) &&
        Regex.IsMatch(sql, @"\bFROM\s+zimmer\s+WHERE\s+id\s*=\s*'?" + roomId.ToString(CultureInfo.InvariantCulture) + @"'?\s+FOR\s+UPDATE\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static async Task Run()
    {
        var fixture = new FixtureSet(FixturePlan.Create());
        Console.WriteLine("RUN " + fixture.Plan.Marker);
        var workers = new List<Worker>();
        var finalStateVerified = false;
        Exception? failure = null;
        try
        {
            fixture.Create();
            Console.WriteLine($"FIXTURES room={fixture.RoomId}; guests={fixture.GuestA},{fixture.GuestB}; bookings={fixture.BookingA},{fixture.BookingB}");
            using (var gateConnection = TestDatabaseGuard.Open())
            using (var gate = gateConnection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
            {
                try
                {
                    var prepared = fixture.ReadOwnedRows(gateConnection, gate);
                    FixtureSet.Require(prepared.Values.All(status => status == "Reserviert"));
                    using (var room = TestDatabaseGuard.Command("SELECT status, geloescht_am FROM zimmer WHERE id = @id", gateConnection, gate))
                    {
                        room.Parameters.AddWithValue("@id", fixture.RoomId);
                        using var reader = room.ExecuteReader();
                        FixtureSet.Require(reader.Read() && reader.GetString(0) == "Verfuegbar" && reader.IsDBNull(1));
                    }
                    // The coordinator holds the own room's row lock while both workers reach the production lock query.
                    using var observer = TestDatabaseGuard.Open(); // Open before GO, outside the contention deadline.
                    workers.Add(Worker.Start(fixture, fixture.Plan.CodeA));
                    workers.Add(Worker.Start(fixture, fixture.Plan.CodeB));
                    await Task.WhenAll(workers.Select(worker => worker.Ready()));
                    await Task.WhenAll(workers.Select(worker => worker.Go()));
                    var observed = await ObserveWorkerQueries(observer, fixture.RoomId, gateConnection.ServerThread);
                    Console.WriteLine($"EVIDENCE: two distinct server sessions ({string.Join(',', observed)}) executing the own room FOR UPDATE while the coordinator holds its lock.");
                    Console.WriteLine("This is gate/query evidence, NOT a direct InnoDB lock-wait graph assertion.");
                }
                finally { gate.Rollback(); } // Release the gate; never commit a coordinator data change.
            }
            await Task.WhenAll(workers.Select(worker => worker.Finish()));
            foreach (var worker in workers) worker.Report();
            FixtureSet.Require(workers.All(worker => worker.State.HasReliableResult));
            var firstResult = workers[0].State.Result ?? throw new InvalidOperationException("Worker result missing.");
            var secondResult = workers[1].State.Result ?? throw new InvalidOperationException("Worker result missing.");
            var firstCommitted = workers[0].State.HasConfirmedCommit;
            var secondCommitted = workers[1].State.HasConfirmedCommit;
            FixtureSet.Require(firstCommitted && workers[1].State.Result == StayTransitionResult.OccupancyConflict ||
                secondCommitted && workers[0].State.Result == StayTransitionResult.OccupancyConflict);
            using var verification = TestDatabaseGuard.Open();
            var statuses = fixture.ReadOwnedRows(verification);
            FixtureSet.Require(statuses.Values.Count(status => status == "Eingecheckt") == 1 && statuses.Values.Count(status => status == "Reserviert") == 1);
            FixtureSet.Require(statuses[fixture.Plan.CodeA] == (firstCommitted ? "Eingecheckt" : "Reserviert"));
            FixtureSet.Require(statuses[fixture.Plan.CodeB] == (secondCommitted ? "Eingecheckt" : "Reserviert"));
            using var roomStatus = TestDatabaseGuard.Command("SELECT status, geloescht_am FROM zimmer WHERE id = @id", verification);
            roomStatus.Parameters.AddWithValue("@id", fixture.RoomId);
            using (var reader = roomStatus.ExecuteReader())
                FixtureSet.Require(reader.Read() && reader.GetString(0) == "Belegt" && reader.IsDBNull(1));
            finalStateVerified = true;
            // A resource-cleanup failure after Commit is known, but must not turn the strict test into PASS.
            FixtureSet.Require(ResultsAreCorrect(firstResult, secondResult));
            Console.WriteLine("ASSERTIONS passed: one Success, one OccupancyConflict; one Eingecheckt, one Reserviert; room Belegt.");
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            // Wait for every owned process to exit, even after coordination/SQL timeout. No live-worker cleanup.
            foreach (var worker in workers)
            {
                try { await worker.StopAndWait(); }
                catch (Exception ex) { worker.State.StopUnconfirmed(); failure ??= ex; }
            }
            var states = workers.Select(worker => worker.State).ToArray();
            if (CleanupSafety.CanCleanup(fixture.Confirmed, finalStateVerified, states))
            {
                try { fixture.Cleanup(states, finalStateVerified); }
                catch (Exception ex)
                {
                    failure ??= ex;
                    fixture.ReportRetained("Cleanup unconfirmed; inspect actual state. No automatic retry.");
                }
            }
            else
            {
                failure ??= new InvalidOperationException("Fixture, worker outcome, exit or final state unconfirmed.");
                fixture.ReportRetained("Automatic cleanup forbidden: preserve diagnostic data.");
            }
            foreach (var worker in workers)
            {
                worker.Report();
                try { worker.Dispose(); } catch (Exception ex) { failure ??= ex; }
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS: real B2.1 scenario and scoped cleanup completed.");
    }

    private static async Task<int[]> ObserveWorkerQueries(MySqlConnection observer, int roomId, int gateThread)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            using var command = TestDatabaseGuard.Command("SELECT ID, INFO FROM information_schema.PROCESSLIST WHERE USER = @user AND DB = DATABASE() AND ID <> CONNECTION_ID() AND ID <> @gate", observer);
            command.Parameters.AddWithValue("@user", "nest_test_runner"); command.Parameters.AddWithValue("@gate", gateThread);
            var ids = new HashSet<int>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (!reader.IsDBNull(1) && IsRoomLockStatement(reader.GetString(1), roomId)) ids.Add(reader.GetInt32(0));
            if (ids.Count == 2) return ids.Order().ToArray();
            FixtureSet.Require(ids.Count < 2);
            // Poll a read-only observable condition, not a timing-based assumption about lock acquisition.
            await Task.Delay(50, deadline.Token);
        }
    }

    private sealed class Worker(Process process, string code) : IDisposable
    {
        internal WorkerRunState State { get; } = new(code);
        internal static Worker Start(FixtureSet fixture, string code)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "--execute-b21", "--worker", fixture.Plan.Marker, code, fixture.RoomId.ToString(CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(argument); // Only synthetic identifiers; never credentials in arguments.
            var child = Process.Start(start) ?? throw new InvalidOperationException("Worker not started.");
            return new Worker(child, code);
        }
        internal async Task Ready()
        {
            // The controller has registered both processes before any further operation can throw.
            try
            {
                process.ErrorDataReceived += (_, _) => { }; // Drain, but never forward driver/credential details.
                process.BeginErrorReadLine();
                FixtureSet.Require(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) == "READY");
            }
            catch (Exception ex) { State.ObserveFailure(ex); throw; }
        }
        internal async Task Go()
        {
            try
            {
                await process.StandardInput.WriteLineAsync("GO");
                await process.StandardInput.FlushAsync();
            }
            catch (Exception ex) { State.ObserveFailure(ex); throw; }
        }
        internal Task Finish() => State.ReadCompletion(
            async () => await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45)),
            () => process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)),
            () => process.ExitCode);

        internal async Task StopAndWait()
        {
            try
            {
                var forced = !process.HasExited;
                if (forced) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                FixtureSet.Require(process.HasExited);
                State.ConfirmStopped(forced);
            }
            catch (Exception ex) { State.ObserveFailure(ex); State.StopUnconfirmed(); throw; }
        }
        internal void Report() => Console.WriteLine($"WORKER code={State.Code}; outcome={State.Outcome}; reportedResult={State.Result?.ToString() ?? "none"}; exitConfirmed={State.ExitConfirmed}");
        public void Dispose() => process.Dispose();
    }
}
