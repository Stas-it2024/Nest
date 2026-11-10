using Nest.Data;

internal enum B22Operation { CheckIn, Cancel }

// Keep the audited B2.1 lifecycle decoder unchanged. Cancellation's typed business results
// are normalized ONLY for lifecycle classification; the actual API result is stored separately.
internal sealed class B22WorkerState(string id, B22Operation operation, string reservationCode)
{
    private readonly WorkerRunState lifecycle = new(id);
    internal string Id { get; } = id;
    internal B22Operation Operation { get; } = operation;
    internal string ReservationCode { get; } = reservationCode;
    internal StayTransitionResult? CheckInResult { get; private set; }
    internal CancellationResult? CancelResult { get; private set; }
    internal WorkerOutcome Outcome => lifecycle.Outcome;
    internal bool ExitConfirmed => lifecycle.ExitConfirmed;
    internal bool HasReliableResult => lifecycle.HasReliableResult && (Operation switch
    {
        B22Operation.CheckIn => CheckInResult != null,
        B22Operation.Cancel => CancelResult != null,
        _ => false
    });
    internal bool HasConfirmedCommit => HasReliableResult && lifecycle.HasConfirmedCommit;

    internal Task ReadCompletion(Func<Task<string?>> readLine, Func<Task> waitForExit, Func<int> exitCode)
    {
        var firstLine = true;
        return lifecycle.ReadCompletion(async () =>
        {
            var line = await readLine();
            var store = firstLine;
            firstLine = false;
            if (line is null or "RESULT COMMIT_UNCERTAIN" or "RESULT TECHNICAL_ERROR") return line;
            if (Operation == B22Operation.CheckIn)
            {
                foreach (var value in Enum.GetValues<StayTransitionResult>())
                    if (line == "RESULT " + value)
                    {
                        if (store) CheckInResult = value;
                        return line;
                    }
            }
            else if (Operation == B22Operation.Cancel)
            {
                foreach (var value in Enum.GetValues<CancellationResult>())
                    if (line == "RESULT " + value)
                    {
                        if (store) CancelResult = value;
                        var category = value switch
                        {
                            CancellationResult.Success => StayTransitionResult.Success,
                            CancellationResult.CommittedWithCleanupFailure => StayTransitionResult.CommittedWithCleanupFailure,
                            CancellationResult.ReservationChangedOrNotFound or CancellationResult.RoomNotFound or
                                CancellationResult.RequiresCheckOut => StayTransitionResult.UpdateRefused,
                            _ => (StayTransitionResult)(-1)
                        };
                        return "RESULT " + category;
                    }
            }
            return "INVALID B2.2 RESULT";
        }, waitForExit, exitCode);
    }

    internal void ConfirmStopped(bool forced) => lifecycle.ConfirmStopped(forced);
    internal void StopUnconfirmed() => lifecycle.StopUnconfirmed();
    internal void ObserveFailure(Exception error) => lifecycle.ObserveFailure(error);
    internal void Report() => Console.WriteLine($"B22 WORKER id={Id}; operation={Operation}; code={ReservationCode}; outcome={Outcome}; reportedResult={CheckInResult?.ToString() ?? CancelResult?.ToString() ?? "none"}; exitConfirmed={ExitConfirmed}");
}

internal static class B22Safety
{
    internal static bool PairIsValid(IReadOnlyList<B22WorkerState> workers) => workers.Count == 2 &&
        workers[0].Id == "A" && workers[0].Operation == B22Operation.CheckIn &&
        workers[1].Id == "B" && workers[1].Operation == B22Operation.Cancel &&
        !string.IsNullOrEmpty(workers[0].ReservationCode) && workers[0].ReservationCode == workers[1].ReservationCode;

    internal static bool CanCleanup(bool fixtureConfirmed, bool finalStateVerified, IReadOnlyList<B22WorkerState> workers) =>
        fixtureConfirmed && finalStateVerified && ResultPairIsValid(workers);

    private static bool ResultPairIsValid(IReadOnlyList<B22WorkerState> workers) => PairIsValid(workers) &&
        workers.All(worker => worker.HasReliableResult) &&
        (workers[0].HasConfirmedCommit && workers[1].CancelResult == CancellationResult.RequiresCheckOut ||
            workers[1].HasConfirmedCommit && workers[0].CheckInResult == StayTransitionResult.ReservationChanged);

    internal static void RequireCleanup(bool fixtureConfirmed, bool finalStateVerified, IReadOnlyList<B22WorkerState> workers)
    {
        if (!CanCleanup(fixtureConfirmed, finalStateVerified, workers))
            throw new InvalidOperationException("B2.2 cleanup forbidden: setup, workers, exits, outcomes or final state unconfirmed.");
    }

    internal static bool StateIsConsistent(IReadOnlyList<B22WorkerState> workers, string booking, string room, bool deleted)
    {
        if (!ResultPairIsValid(workers) || deleted) return false;
        return workers[0].HasConfirmedCommit && workers[1].CancelResult == CancellationResult.RequiresCheckOut && booking == "Eingecheckt" && room == "Belegt" ||
            workers[1].HasConfirmedCommit && workers[0].CheckInResult == StayTransitionResult.ReservationChanged && booking == "Storniert" && room == "Verfuegbar";
    }

    internal static bool IsPass(IReadOnlyList<B22WorkerState> workers, string booking, string room, bool deleted) =>
        StateIsConsistent(workers, booking, room, deleted) &&
        (workers[0].CheckInResult == StayTransitionResult.Success || workers[1].CancelResult == CancellationResult.Success);
}
