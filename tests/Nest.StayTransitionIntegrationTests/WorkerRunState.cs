using Nest.Data;

internal enum WorkerOutcome
{
    NoReliableResult,
    ConfirmedSuccess,
    ConfirmedBusinessRefusal,
    CommittedWithCleanupFailure,
    CommitUncertain,
    TechnicalErrorUnknown,
    TimedOut,
    Crashed,
    InvalidResult
}

// A received line alone is not confirmation: the matching normal process exit is required too.
internal sealed class WorkerRunState(string code)
{
    internal string Code { get; } = code;
    internal WorkerOutcome Outcome { get; private set; } = WorkerOutcome.NoReliableResult;
    internal StayTransitionResult? Result { get; private set; }
    internal bool ExitConfirmed { get; private set; }
    internal bool HasReliableResult => ExitConfirmed && Result != null && Outcome is
        WorkerOutcome.ConfirmedSuccess or WorkerOutcome.ConfirmedBusinessRefusal or WorkerOutcome.CommittedWithCleanupFailure;
    internal bool HasConfirmedCommit => HasReliableResult && Outcome is
        WorkerOutcome.ConfirmedSuccess or WorkerOutcome.CommittedWithCleanupFailure;

    internal static int ResultExitCode(StayTransitionResult result) =>
        Enum.IsDefined(result) ? 0 : 2;

    internal async Task ReadCompletion(Func<Task<string?>> readLine, Func<Task> waitForExit, Func<int> exitCode)
    {
        // Never permit a second read/retry to overwrite a previous uncertain outcome.
        if (Outcome != WorkerOutcome.NoReliableResult || Result != null || ExitConfirmed)
            throw new InvalidOperationException("Worker completion already observed.");
        try
        {
            var line = await readLine();
            var expectedExit = 0;
            if (line == "RESULT COMMIT_UNCERTAIN")
            {
                Outcome = WorkerOutcome.CommitUncertain;
                expectedExit = 2;
            }
            else if (line == "RESULT TECHNICAL_ERROR")
            {
                Outcome = WorkerOutcome.TechnicalErrorUnknown;
                expectedExit = 2;
            }
            else if (TryReadRepositoryResult(line, out var result)) Result = result;
            else Outcome = line == null ? WorkerOutcome.NoReliableResult : WorkerOutcome.InvalidResult;

            await waitForExit();
            ExitConfirmed = true;
            var extra = await readLine();
            if (extra != null)
            {
                if (extra == "RESULT COMMIT_UNCERTAIN") Outcome = WorkerOutcome.CommitUncertain;
                else MarkFailure(WorkerOutcome.InvalidResult);
                return; // More than one terminal message is not a trustworthy protocol result.
            }
            if (exitCode() != expectedExit) MarkFailure(WorkerOutcome.Crashed);
            else if (Result is { } confirmed)
                Outcome = confirmed switch
                {
                    StayTransitionResult.Success => WorkerOutcome.ConfirmedSuccess,
                    StayTransitionResult.CommittedWithCleanupFailure => WorkerOutcome.CommittedWithCleanupFailure,
                    StayTransitionResult.ReservationChanged or StayTransitionResult.UnsuitableRoom or
                        StayTransitionResult.OccupancyConflict or StayTransitionResult.UpdateRefused => WorkerOutcome.ConfirmedBusinessRefusal,
                    _ => WorkerOutcome.InvalidResult
                };
        }
        catch (TimeoutException) { MarkFailure(WorkerOutcome.TimedOut); }
        catch { MarkFailure(WorkerOutcome.TechnicalErrorUnknown); }
    }

    private static bool TryReadRepositoryResult(string? line, out StayTransitionResult result)
    {
        // Reject numeric enum values, comma lists, whitespace variants and undefined values.
        foreach (var value in Enum.GetValues<StayTransitionResult>())
            if (line == "RESULT " + value) { result = value; return true; }
        result = default;
        return false;
    }

    private void MarkFailure(WorkerOutcome outcome)
    {
        // Lost acknowledgement remains uncertain even if a later wait/exit/kill also fails.
        if (Outcome != WorkerOutcome.CommitUncertain) Outcome = outcome;
    }

    internal void ConfirmStopped(bool forced)
    {
        ExitConfirmed = true;
        // Forced shutdown cannot make an unfinished observation reliable.
        if (forced && Outcome is WorkerOutcome.NoReliableResult or WorkerOutcome.ConfirmedSuccess or
            WorkerOutcome.ConfirmedBusinessRefusal or WorkerOutcome.CommittedWithCleanupFailure)
            MarkFailure(WorkerOutcome.Crashed);
    }

    internal void StopUnconfirmed() => ExitConfirmed = false;

    internal void ObserveFailure(Exception exception) =>
        MarkFailure(exception is TimeoutException ? WorkerOutcome.TimedOut : WorkerOutcome.TechnicalErrorUnknown);
}

internal static class CleanupSafety
{
    internal static bool CanCleanup(bool fixtureConfirmed, bool finalStateVerified, IReadOnlyList<WorkerRunState> workers) =>
        fixtureConfirmed && finalStateVerified && workers.Count == 2 && !ReferenceEquals(workers[0], workers[1]) &&
        workers[0].Code != workers[1].Code && workers.All(worker => worker.HasReliableResult);

    internal static void RequireAllowed(bool fixtureConfirmed, bool finalStateVerified, IReadOnlyList<WorkerRunState> workers)
    {
        if (!CanCleanup(fixtureConfirmed, finalStateVerified, workers))
            throw new InvalidOperationException("Automatic cleanup denied: fixture, worker outcome, exit or final-state verification is unconfirmed.");
    }
}
