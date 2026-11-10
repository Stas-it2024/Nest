using Nest.Data;
using System.Globalization;
using System.IO;

internal static class B22OfflineChecks
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool value) { if (!value) throw new InvalidOperationException("B2.2 offline safety invariant failed."); checks++; }
        static B22WorkerState Read(string id, B22Operation operation, string? message, int exit = 0, string code = "B22OFFLINE", string? extra = null)
        {
            var state = new B22WorkerState(id, operation, code);
            var messages = new Queue<string?>(new[] { message, extra });
            state.ReadCompletion(() => Task.FromResult(messages.Count == 0 ? null : messages.Dequeue()),
                () => Task.CompletedTask, () => exit).GetAwaiter().GetResult();
            return state;
        }
        static B22WorkerState A(StayTransitionResult result) => Read("A", B22Operation.CheckIn, "RESULT " + result);
        static B22WorkerState B(CancellationResult result) => Read("B", B22Operation.Cancel, "RESULT " + result);
        static B22WorkerState Fault(string id, B22Operation operation, Exception error, bool afterResult = false)
        {
            var state = new B22WorkerState(id, operation, "B22OFFLINE");
            state.ReadCompletion(() => afterResult ? Task.FromResult<string?>("RESULT Success") : Task.FromException<string?>(error),
                () => Task.FromException(error), () => 0).GetAwaiter().GetResult();
            return state;
        }
        var checkInWins = new[] { A(StayTransitionResult.Success), B(CancellationResult.RequiresCheckOut) };
        var cancelWins = new[] { A(StayTransitionResult.ReservationChanged), B(CancellationResult.Success) };
        Check(B22Safety.IsPass(checkInWins, "Eingecheckt", "Belegt", false));
        Check(B22Safety.IsPass(cancelWins, "Storniert", "Verfuegbar", false));
        Check(!B22Safety.CanCleanup(true, true, new[] { A(StayTransitionResult.Success), B(CancellationResult.Success) }));
        Check(B22Safety.CanCleanup(true, true, checkInWins) && B22Safety.CanCleanup(true, true, cancelWins));

        foreach (var a in Enum.GetValues<StayTransitionResult>())
        foreach (var b in Enum.GetValues<CancellationResult>())
        foreach (var booking in new[] { "Reserviert", "Eingecheckt", "Ausgecheckt", "Storniert" })
        foreach (var room in new[] { "Verfuegbar", "Belegt", "Reserviert", "Wartung" })
        foreach (var deleted in new[] { false, true })
        {
            var states = new[] { A(a), B(b) };
            var expected = !deleted && (a == StayTransitionResult.Success && b == CancellationResult.RequiresCheckOut && booking == "Eingecheckt" && room == "Belegt" ||
                b == CancellationResult.Success && a == StayTransitionResult.ReservationChanged && booking == "Storniert" && room == "Verfuegbar");
            var consistent = !deleted && (a is StayTransitionResult.Success or StayTransitionResult.CommittedWithCleanupFailure && b == CancellationResult.RequiresCheckOut && booking == "Eingecheckt" && room == "Belegt" ||
                b is CancellationResult.Success or CancellationResult.CommittedWithCleanupFailure && a == StayTransitionResult.ReservationChanged && booking == "Storniert" && room == "Verfuegbar");
            Check(B22Safety.IsPass(states, booking, room, deleted) == expected);
            Check(B22Safety.StateIsConsistent(states, booking, room, deleted) == consistent);
            Check(B22Safety.CanCleanup(true, consistent, states) == consistent);
        }
        foreach (var pair in new[]
        {
            new[] { A(StayTransitionResult.CommittedWithCleanupFailure), B(CancellationResult.RequiresCheckOut) },
            new[] { A(StayTransitionResult.ReservationChanged), B(CancellationResult.CommittedWithCleanupFailure) }
        })
        {
            Check(pair.Any(worker => worker.Outcome == WorkerOutcome.CommittedWithCleanupFailure && worker.HasConfirmedCommit));
            Check(B22Safety.CanCleanup(true, true, pair));
            Check(!B22Safety.IsPass(pair, pair[0].HasConfirmedCommit ? "Eingecheckt" : "Storniert", pair[0].HasConfirmedCommit ? "Belegt" : "Verfuegbar", false));
        }

        foreach (var operation in Enum.GetValues<B22Operation>())
        {
            var id = operation == B22Operation.CheckIn ? "A" : "B";
            foreach (var item in new[]
            {
                ("RESULT COMMIT_UNCERTAIN", 2, WorkerOutcome.CommitUncertain),
                ("RESULT TECHNICAL_ERROR", 2, WorkerOutcome.TechnicalErrorUnknown),
                ((string?)null, 0, WorkerOutcome.NoReliableResult),
                ((string?)null, 137, WorkerOutcome.Crashed),
                ("RESULT Success", 1, WorkerOutcome.Crashed),
                ("RESULT 0", 0, WorkerOutcome.InvalidResult),
                ("RESULT Unknown", 0, WorkerOutcome.InvalidResult),
                ("RESULT Success ", 0, WorkerOutcome.InvalidResult),
                ("RESULT success", 0, WorkerOutcome.InvalidResult),
                ("RESULT Success,RequiresCheckOut", 0, WorkerOutcome.InvalidResult)
            })
            {
                var state = Read(id, operation, item.Item1, item.Item2);
                state.ConfirmStopped(false);
                Check(state.ExitConfirmed && state.Outcome == item.Item3 && !state.HasReliableResult);
                var pair = operation == B22Operation.CheckIn ? new[] { state, B(CancellationResult.RequiresCheckOut) } : new[] { A(StayTransitionResult.ReservationChanged), state };
                Check(!B22Safety.CanCleanup(true, true, pair));
                var destructiveCalls = 0;
                try { B22Safety.RequireCleanup(true, true, pair); destructiveCalls++; }
                catch (InvalidOperationException) { Check(true); }
                Check(destructiveCalls == 0);
            }
            foreach (var fault in new[] { Fault(id, operation, new TimeoutException()), Fault(id, operation, new TimeoutException(), true), Fault(id, operation, new IOException()) })
            {
                fault.ConfirmStopped(false); // A later confirmed exit must not erase the missing reliable result.
                var pair = operation == B22Operation.CheckIn ? new[] { fault, B(CancellationResult.RequiresCheckOut) } : new[] { A(StayTransitionResult.ReservationChanged), fault };
                Check(!fault.HasReliableResult && !B22Safety.CanCleanup(true, true, pair));
            }
            var duplicate = Read(id, operation, "RESULT Success", extra: "RESULT Success");
            var contradictory = Read(id, operation, "RESULT Success", extra: "RESULT COMMIT_UNCERTAIN");
            Check(duplicate.Outcome == WorkerOutcome.InvalidResult && !duplicate.HasReliableResult);
            Check(contradictory.Outcome == WorkerOutcome.CommitUncertain && !contradictory.HasReliableResult);
            contradictory.ObserveFailure(new TimeoutException()); contradictory.ConfirmStopped(true);
            Check(contradictory.Outcome == WorkerOutcome.CommitUncertain);
            var unknownExit = Read(id, operation, "RESULT Success"); unknownExit.StopUnconfirmed();
            Check(!unknownExit.HasReliableResult);
            var unknownExitPair = operation == B22Operation.CheckIn ? new[] { unknownExit, B(CancellationResult.RequiresCheckOut) } : new[] { A(StayTransitionResult.ReservationChanged), unknownExit };
            Check(!B22Safety.CanCleanup(true, true, unknownExitPair));
            var killed = Read(id, operation, "RESULT Success"); killed.ConfirmStopped(true);
            Check(killed.Outcome == WorkerOutcome.Crashed && !killed.HasReliableResult);
        }
        Check(!Read("B", B22Operation.Cancel, "RESULT OccupancyConflict").HasReliableResult);
        Check(!Read("A", B22Operation.CheckIn, "RESULT RequiresCheckOut").HasReliableResult);
        var bothTimeouts = new[] { Fault("A", B22Operation.CheckIn, new TimeoutException()), Fault("B", B22Operation.Cancel, new TimeoutException()) };
        foreach (var state in bothTimeouts) state.ConfirmStopped(false);
        Check(!B22Safety.CanCleanup(true, true, bothTimeouts));
        var bothUncertain = new[] { Read("A", B22Operation.CheckIn, "RESULT COMMIT_UNCERTAIN", 2), Read("B", B22Operation.Cancel, "RESULT COMMIT_UNCERTAIN", 2) };
        Check(!B22Safety.CanCleanup(true, true, bothUncertain));
        Check(!B22Safety.CanCleanup(false, true, checkInWins));
        Check(!B22Safety.CanCleanup(true, false, checkInWins));
        Check(!B22Safety.CanCleanup(true, true, Array.Empty<B22WorkerState>()));
        Check(!B22Safety.CanCleanup(true, true, new[] { checkInWins[0] }));
        Check(!B22Safety.CanCleanup(true, true, new[] { checkInWins[0], checkInWins[0] }));
        Check(!B22Safety.CanCleanup(true, true, checkInWins.Reverse().ToArray()));
        Check(!B22Safety.CanCleanup(true, true, new[] { checkInWins[0], Read("B", B22Operation.Cancel, "RESULT RequiresCheckOut", code: "OTHER") }));

        for (var i = 0; i < 100; i++)
        {
            var plan = B22FixturePlan.Create(); B22FixturePlan.ValidateWorker(plan.Marker, plan.Code, 42);
            Check(plan.Marker.Length <= 50 && plan.Code.Length == 20 && plan.Email.Length <= 100);
            Check(plan.RoomNumber.ToString(CultureInfo.InvariantCulture).Length <= 10 && plan.RoomNumber > 0);
            Check(plan.Today == DateTime.Today && plan.Today.AddDays(1) > plan.Today);
        }
        var fixturePlan = B22FixturePlan.Create();
        foreach (var invalid in new[] { (fixturePlan.Marker, "wrong", 42), ("Nest-B21-" + fixturePlan.Token, fixturePlan.Code, 42), (fixturePlan.Marker, fixturePlan.Code, 0) })
        {
            try { B22FixturePlan.ValidateWorker(invalid.Item1, invalid.Item2, invalid.Item3); throw new Exception("Invalid fixture accepted"); }
            catch (InvalidOperationException) { Check(true); }
        }
        Check(ConcurrentCancellationScenario.RoomLockOperation("SELECT id, status, geloescht_am FROM zimmer WHERE id = 42 FOR UPDATE;", 42) == B22Operation.CheckIn);
        Check(ConcurrentCancellationScenario.RoomLockOperation("SELECT id FROM zimmer WHERE id = 42 FOR UPDATE;", 42) == B22Operation.Cancel);
        foreach (var sql in new[] { "SELECT id FROM zimmer WHERE id = 420 FOR UPDATE;", "SELECT id FROM zimmer WHERE id = 42;", "SELECT id FROM reservierungen WHERE id = 42 FOR UPDATE;", "SELECT id FROM zimmer WHERE id = 42 FOR UPDATE; UPDATE zimmer SET status='Belegt'" })
            Check(ConcurrentCancellationScenario.RoomLockOperation(sql, 42) == null);
        Check(ConcurrentCancellationScenario.ObservationIsValid(new Dictionary<B22Operation, int> { [B22Operation.CheckIn] = 10, [B22Operation.Cancel] = 11 }));
        Check(!ConcurrentCancellationScenario.ObservationIsValid(new Dictionary<B22Operation, int> { [B22Operation.CheckIn] = 10, [B22Operation.Cancel] = 10 }));
        Check(!ConcurrentCancellationScenario.ObservationIsValid(new Dictionary<B22Operation, int> { [B22Operation.CheckIn] = 10 }));

        var mode = Environment.GetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable);
        var b21 = Environment.GetEnvironmentVariable(TestDatabaseGuard.WriteVariable);
        var b22 = Environment.GetEnvironmentVariable(B22DatabaseGuard.WriteVariable);
        try
        {
            Environment.SetEnvironmentVariable(TestDatabaseGuard.WriteVariable, IntegrationConnectionSettings.DatabaseName);
            foreach (var hasMode in new[] { false, true })
            foreach (var hasWrite in new[] { false, true })
            foreach (var hasArgument in new[] { false, true })
            {
                Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, hasMode ? "1" : null);
                Environment.SetEnvironmentVariable(B22DatabaseGuard.WriteVariable, hasWrite ? IntegrationConnectionSettings.DatabaseName : null);
                var allowed = false;
                try { B22Entry.RequireExecution(hasArgument ? new[] { "--execute-b22" } : Array.Empty<string>()); allowed = true; }
                catch (InvalidOperationException) { }
                Check(allowed == (hasMode && hasWrite && hasArgument));
            }
            foreach (var wrongMode in new[] { "", "0", "true", "wrong" })
            {
                Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, wrongMode);
                Environment.SetEnvironmentVariable(B22DatabaseGuard.WriteVariable, IntegrationConnectionSettings.DatabaseName);
                try { B22Entry.RequireExecution(new[] { "--execute-b22" }); throw new Exception("Unsafe mode accepted"); }
                catch (InvalidOperationException) { Check(true); }
            }
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, "1");
            foreach (var wrongWrite in new string?[] { null, "", "hotel", "1", "true", "nest_integration_test " })
            {
                Environment.SetEnvironmentVariable(B22DatabaseGuard.WriteVariable, wrongWrite);
                try { B22Entry.RequireExecution(new[] { "--execute-b22" }); throw new Exception("Unsafe write flag accepted"); }
                catch (InvalidOperationException) { Check(true); }
            }
            Environment.SetEnvironmentVariable(B22DatabaseGuard.WriteVariable, IntegrationConnectionSettings.DatabaseName);
            var workerArgs = new[] { "--execute-b22", "--worker", fixturePlan.Marker, fixturePlan.Code, "42", "A", "CheckIn" };
            B22Entry.RequireExecution(workerArgs); Check(B22Entry.IsWorkerRequest(workerArgs));
            workerArgs[5] = "B"; workerArgs[6] = "Cancel";
            B22Entry.RequireExecution(workerArgs); Check(B22Entry.IsWorkerRequest(workerArgs));
            foreach (var args in new[] { workerArgs.Skip(1).ToArray(), new[] { "--execute-b21" }, new[] { "--execute-b22", "extra" },
                new[] { "--execute-b22", "--worker", fixturePlan.Marker, fixturePlan.Code, "42", "A", "Cancel" } })
            {
                try { B22Entry.RequireExecution(args); throw new Exception("Unsafe arguments accepted"); }
                catch (InvalidOperationException) { Check(true); }
            }
            Environment.SetEnvironmentVariable(TestDatabaseGuard.WriteVariable, null);
            try { TestDatabaseGuard.RequireAuthorization(); throw new Exception("B2.2 authorized B2.1"); }
            catch (InvalidOperationException) { Check(true); }
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrationConnectionSettings.ModeVariable, mode);
            Environment.SetEnvironmentVariable(TestDatabaseGuard.WriteVariable, b21);
            Environment.SetEnvironmentVariable(B22DatabaseGuard.WriteVariable, b22);
        }
        // Explicit non-regression checks for B2.1's original admission/selection semantics.
        Check(!B22Entry.IsRequest(new[] { "--execute-b21" }) && !B22Entry.IsRequest(new[] { "--self-test" }) && !B22Entry.IsRequest(Array.Empty<string>()));
        Check(!ConcurrentCheckInScenario.IsRoomLockStatement("SELECT id FROM zimmer WHERE id = 42 FOR UPDATE;", 42));
        Check(ConcurrentCheckInScenario.ResultsAreCorrect(StayTransitionResult.Success, StayTransitionResult.OccupancyConflict));
        Check(!ConcurrentCheckInScenario.ResultsAreCorrect(StayTransitionResult.Success, StayTransitionResult.Success));
        Console.WriteLine($"PASS B2.2: {checks} offline matrix/lifecycle/fixture/admission/observation checks; no credentials read, connections opened, SQL writes or Repository operations.");
    }
}
