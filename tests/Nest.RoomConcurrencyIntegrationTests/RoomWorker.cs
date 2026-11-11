using Nest.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

internal enum RoomOutcome { Unknown, Success, BusinessRefusal, CleanupFailure, CommitUncertain, TechnicalUnknown, TimedOut, Crashed, Invalid }

internal sealed class RoomWorkerState(string key)
{
    private bool terminalObserved;
    internal string Key { get; } = key;
    internal RoomOutcome Outcome { get; private set; } = RoomOutcome.Unknown;
    internal RoomWriteResult? Result { get; private set; }
    internal bool ExitConfirmed { get; private set; }
    internal bool Reliable => ExitConfirmed && Result != null && Outcome is RoomOutcome.Success or RoomOutcome.BusinessRefusal or RoomOutcome.CleanupFailure;
    internal bool Committed => Reliable && Outcome is RoomOutcome.Success or RoomOutcome.CleanupFailure;
    internal static int ExpectedExit(RoomWriteResult result) => result is RoomWriteResult.TechnicalFailure or RoomWriteResult.CommitUncertain or RoomWriteResult.UncertainOutcome ? 2 : Enum.IsDefined(result) ? 0 : 2;
    internal void Complete(string text, int exitCode, bool stopped)
    {
        Safety.Require(!terminalObserved && Outcome == RoomOutcome.Unknown && Result == null && !ExitConfirmed);
        terminalObserved = true;
        ExitConfirmed = stopped;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
        // An uncertain message dominates even a second/malformed terminal message.
        if (lines.Contains("RESULT " + RoomWriteResult.CommitUncertain, StringComparer.Ordinal))
        { Result = RoomWriteResult.CommitUncertain; Outcome = RoomOutcome.CommitUncertain; return; }
        if (lines.Count != 1) { Outcome = lines.Count == 0 ? RoomOutcome.Unknown : RoomOutcome.Invalid; return; }
        if (lines[0] == "RESULT TECHNICAL_ERROR") { Outcome = RoomOutcome.TechnicalUnknown; return; }
        foreach (var value in Enum.GetValues<RoomWriteResult>())
            if (lines[0] == "RESULT " + value) { Result = value; break; }
        if (Result == null) { Outcome = RoomOutcome.Invalid; return; }
        if (exitCode != ExpectedExit(Result.Value)) { Outcome = RoomOutcome.Crashed; return; }
        Outcome = Result switch {
            RoomWriteResult.Success => RoomOutcome.Success,
            RoomWriteResult.CommittedWithCleanupFailure => RoomOutcome.CleanupFailure,
            RoomWriteResult.Missing or RoomWriteResult.ConcurrencyConflict or RoomWriteResult.Occupied or
                RoomWriteResult.FutureReservation or RoomWriteResult.InvalidStatus or RoomWriteResult.NumberConflict => RoomOutcome.BusinessRefusal,
            _ => RoomOutcome.TechnicalUnknown
        };
    }
    internal void Fail(Exception exception)
    { if (Outcome != RoomOutcome.CommitUncertain) Outcome = exception is TimeoutException ? RoomOutcome.TimedOut : RoomOutcome.TechnicalUnknown; }
    internal void ConfirmStopped(bool forced)
    {
        ExitConfirmed = true;
        if (forced && Outcome != RoomOutcome.CommitUncertain) Outcome = RoomOutcome.Crashed;
    }
    internal void StopUnconfirmed() => ExitConfirmed = false;
    internal static bool CanCleanup(bool created, bool finalVerified, IReadOnlyList<RoomWorkerState> workers, int expectedCount) =>
        created && finalVerified && expectedCount is 1 or 2 && workers.Count == expectedCount &&
        workers.Select(w => w.Key).Distinct(StringComparer.Ordinal).Count() == expectedCount &&
        workers.All(w => (w.Key is "A" or "B") && w.Reliable);
}

internal sealed class RoomWorker(Process process, string key) : IDisposable
{
    internal RoomWorkerState State { get; } = new(key);
    internal static RoomWorker Start(RoomFixture fixture, RoomOperation operation, string key)
    {
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, Program.ExecuteFlag, "--worker",
            fixture.Plan.Marker, fixture.Id.ToString(CultureInfo.InvariantCulture), fixture.Plan.Number.ToString(CultureInfo.InvariantCulture), operation.ToString(), key })
            start.ArgumentList.Add(argument); // Only own synthetic identifiers, no baseline/credentials in arguments.
        start.Environment.Remove("NEST_ALLOW_B21_WRITES"); start.Environment.Remove("NEST_ALLOW_B22_WRITES");
        return new(Process.Start(start) ?? throw new InvalidOperationException(), key);
    }
    internal async Task Ready(string fingerprint)
    {
        try
        {
            process.ErrorDataReceived += (_, _) => { }; // Drain without forwarding potentially sensitive diagnostics.
            process.BeginErrorReadLine();
            Safety.Require(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) == "READY " + fingerprint);
        }
        catch (Exception ex) { State.Fail(ex); throw; }
    }
    internal async Task Go()
    {
        try
        {
            await process.StandardInput.WriteLineAsync("GO").WaitAsync(TimeSpan.FromSeconds(5));
            await process.StandardInput.FlushAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) { State.Fail(ex); throw; }
    }
    internal async Task Finish()
    {
        try
        {
            var text = await process.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            State.Complete(text, process.ExitCode, process.HasExited);
        }
        catch (Exception ex) { State.Fail(ex); throw; }
    }
    internal async Task StopAndWait()
    {
        try
        {
            var forced = !process.HasExited;
            if (forced) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Safety.Require(process.HasExited);
            State.ConfirmStopped(forced);
        }
        catch (Exception ex) { State.Fail(ex); State.StopUnconfirmed(); throw; }
    }
    internal void Report() => Console.WriteLine($"WORKER key={State.Key}; pid={process.Id}; outcome={State.Outcome}; result={State.Result?.ToString() ?? "none"}; exitConfirmed={State.ExitConfirmed}");
    public void Dispose() => process.Dispose();
}
