using Nest.Data;
using System.IO;

internal static class WorkerSafetyChecks
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool value)
        {
            if (!value) throw new InvalidOperationException("Worker cleanup safety invariant failed.");
            checks++;
        }

        static WorkerRunState Read(string code, string? line, int exit = 0, string? extra = null)
        {
            var state = new WorkerRunState(code);
            var messages = new Queue<string?>(new[] { line, extra });
            state.ReadCompletion(() => Task.FromResult(messages.Count > 0 ? messages.Dequeue() : null),
                () => Task.CompletedTask, () => exit).GetAwaiter().GetResult();
            return state;
        }

        static WorkerRunState Fault(string code, Exception error, bool afterLine = false, string line = "RESULT Success")
        {
            var state = new WorkerRunState(code);
            state.ReadCompletion(
                () => afterLine ? Task.FromResult<string?>(line) : Task.FromException<string?>(error),
                () => Task.FromException(error), () => 0).GetAwaiter().GetResult();
            return state;
        }

        var success = Read("A", "RESULT Success");
        var refusal = Read("B", "RESULT OccupancyConflict");
        var cleanupFailure = Read("A", "RESULT CommittedWithCleanupFailure");
        Check(success.Outcome == WorkerOutcome.ConfirmedSuccess && success.HasConfirmedCommit);
        Check(refusal.Outcome == WorkerOutcome.ConfirmedBusinessRefusal && !refusal.HasConfirmedCommit);
        Check(cleanupFailure.Outcome == WorkerOutcome.CommittedWithCleanupFailure && cleanupFailure.HasConfirmedCommit);
        Check(CleanupSafety.CanCleanup(true, true, new[] { success, refusal }));
        Check(CleanupSafety.CanCleanup(true, true, new[] { cleanupFailure, refusal }));
        Check(!ConcurrentCheckInScenario.ResultsAreCorrect(StayTransitionResult.CommittedWithCleanupFailure, StayTransitionResult.OccupancyConflict));

        // Exercise the real decoder and cleanup gate, not a copy of either algorithm.
        foreach (var result in Enum.GetValues<StayTransitionResult>())
        {
            var state = Read("A", "RESULT " + result, WorkerRunState.ResultExitCode(result));
            Check(state.HasReliableResult && state.ExitConfirmed && state.Result == result);
            Check(state.HasConfirmedCommit == (result is StayTransitionResult.Success or StayTransitionResult.CommittedWithCleanupFailure));
        }
        Check(WorkerRunState.ResultExitCode((StayTransitionResult)999) == 2);

        var uncertainA = Read("A", "RESULT COMMIT_UNCERTAIN", 2);
        var uncertainB = Read("B", "RESULT COMMIT_UNCERTAIN", 2);
        Check(uncertainA.ExitConfirmed && uncertainA.Outcome == WorkerOutcome.CommitUncertain && !uncertainA.HasConfirmedCommit);
        Check(!CleanupSafety.CanCleanup(true, true, new[] { uncertainA, refusal }));
        Check(!CleanupSafety.CanCleanup(true, true, new[] { success, uncertainB }));
        Check(!CleanupSafety.CanCleanup(true, true, new[] { uncertainA, uncertainB }));
        uncertainA.ConfirmStopped(false);
        Check(!CleanupSafety.CanCleanup(true, true, new[] { uncertainA, refusal })); // Both processes exited: still forbidden.
        uncertainA.ObserveFailure(new TimeoutException());
        uncertainA.ConfirmStopped(true);
        Check(uncertainA.Outcome == WorkerOutcome.CommitUncertain && !uncertainA.HasReliableResult);

        var technical = Read("A", "RESULT TECHNICAL_ERROR", 2);
        var lost = Read("A", null);
        var crash = Read("A", null, 137);
        var afterCommitCrash = Read("A", "RESULT Success", 1);
        var readError = Fault("A", new IOException());
        var waitError = Fault("A", new InvalidOperationException(), afterLine: true);
        var timeoutA = Fault("A", new TimeoutException());
        var timeoutB = Fault("B", new TimeoutException());
        var exitTimeout = Fault("A", new TimeoutException(), afterLine: true);
        var uncertainWait = Fault("A", new TimeoutException(), afterLine: true, line: "RESULT COMMIT_UNCERTAIN");
        Check(technical.Outcome == WorkerOutcome.TechnicalErrorUnknown);
        Check(lost.Outcome == WorkerOutcome.NoReliableResult && lost.ExitConfirmed);
        Check(crash.Outcome == WorkerOutcome.Crashed && crash.ExitConfirmed);
        Check(afterCommitCrash.Outcome == WorkerOutcome.Crashed && !afterCommitCrash.HasReliableResult);
        Check(readError.Outcome == WorkerOutcome.TechnicalErrorUnknown);
        Check(waitError.Outcome == WorkerOutcome.TechnicalErrorUnknown && !waitError.HasReliableResult);
        Check(timeoutA.Outcome == WorkerOutcome.TimedOut && timeoutB.Outcome == WorkerOutcome.TimedOut);
        Check(exitTimeout.Outcome == WorkerOutcome.TimedOut && !exitTimeout.HasReliableResult);
        Check(uncertainWait.Outcome == WorkerOutcome.CommitUncertain);
        foreach (var state in new[] { technical, lost, crash, afterCommitCrash, readError, waitError, timeoutA, exitTimeout, uncertainWait })
        {
            state.ConfirmStopped(false);
            Check(!CleanupSafety.CanCleanup(true, true, new[] { state, refusal }));
        }
        Check(!CleanupSafety.CanCleanup(true, true, new[] { success, timeoutB }));
        timeoutB.ConfirmStopped(false);
        Check(!CleanupSafety.CanCleanup(true, true, new[] { timeoutA, timeoutB }));

        foreach (var line in new[] { "", "RESULT", "RESULT 0", "RESULT 999", "RESULT Success ", "RESULT success", "RESULT Success,OccupancyConflict", "garbage" })
        {
            var state = Read("A", line);
            Check(state.Outcome == WorkerOutcome.InvalidResult && !state.HasReliableResult);
            Check(!CleanupSafety.CanCleanup(true, true, new[] { state, refusal }));
        }
        var duplicate = Read("A", "RESULT Success", extra: "RESULT Success");
        var contradictory = Read("A", "RESULT Success", extra: "RESULT COMMIT_UNCERTAIN");
        Check(duplicate.Outcome == WorkerOutcome.InvalidResult && !duplicate.HasReliableResult);
        Check(contradictory.Outcome == WorkerOutcome.CommitUncertain && !contradictory.HasReliableResult);
        Check(!CleanupSafety.CanCleanup(true, true, new[] { duplicate, refusal }));
        Check(!CleanupSafety.CanCleanup(true, true, new[] { contradictory, refusal }));
        var stoppedUnknown = new WorkerRunState("A");
        stoppedUnknown.ConfirmStopped(false);
        Check(!CleanupSafety.CanCleanup(true, true, new[] { stoppedUnknown, refusal }));
        var killed = Read("A", "RESULT Success");
        killed.ConfirmStopped(true);
        Check(killed.Outcome == WorkerOutcome.Crashed && !killed.HasReliableResult);
        var unconfirmedStop = Read("A", "RESULT Success");
        unconfirmedStop.StopUnconfirmed();
        Check(!CleanupSafety.CanCleanup(true, true, new[] { unconfirmedStop, refusal }));
        Check(!CleanupSafety.CanCleanup(false, true, new[] { success, refusal }));
        Check(!CleanupSafety.CanCleanup(true, false, new[] { success, refusal })); // Final status/ownership verification failed.
        Check(!CleanupSafety.CanCleanup(true, true, Array.Empty<WorkerRunState>()));
        Check(!CleanupSafety.CanCleanup(true, true, new[] { success }));
        Check(!CleanupSafety.CanCleanup(true, true, new[] { success, refusal, cleanupFailure }));
        Check(!CleanupSafety.CanCleanup(true, true, new[] { success, success }));
        Check(!CleanupSafety.CanCleanup(true, true, new[] { success, Read("A", "RESULT OccupancyConflict") }));

        // Mutation regression: replacing the first OR second confirmed result by uncertainty must
        // prevent reaching the destructive callback, even with setup/statuses/exits confirmed.
        var destructiveCalls = 0;
        foreach (var pair in new[] { new[] { uncertainA, refusal }, new[] { success, uncertainB } })
        {
            try
            {
                CleanupSafety.RequireAllowed(true, true, pair);
                destructiveCalls++;
            }
            catch (InvalidOperationException) { Check(true); }
        }
        Check(destructiveCalls == 0);
        CleanupSafety.RequireAllowed(true, true, new[] { cleanupFailure, refusal });
        Check(true); // Confirmed cleanup failure is not a CommitUncertain result.

        var cases = new[]
        {
            ("RESULT Success", 0, true), ("RESULT OccupancyConflict", 0, true),
            ("RESULT CommittedWithCleanupFailure", 0, true), ("RESULT COMMIT_UNCERTAIN", 2, false),
            ("RESULT TECHNICAL_ERROR", 2, false), ("RESULT Success", 1, false), ("invalid", 0, false)
        };
        foreach (var first in cases)
        foreach (var second in cases)
        foreach (var fixtureConfirmed in new[] { false, true })
        foreach (var verified in new[] { false, true })
        {
            var a = Read("A", first.Item1, first.Item2);
            var b = Read("B", second.Item1, second.Item2);
            Check(CleanupSafety.CanCleanup(fixtureConfirmed, verified, new[] { a, b }) ==
                (fixtureConfirmed && verified && first.Item3 && second.Item3));
        }
        return checks;
    }
}
