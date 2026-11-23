using Nest.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

internal enum Operation { Buy, Seed, Shrink, Deactivate, Rename }
internal enum Outcome { Unknown, Success, BusinessRefusal, CleanupFailure, CommitUncertain, RollbackUncertain, TechnicalUnknown, TimedOut, Crashed, Invalid }
internal sealed record WorkerPacket(string Key, string Operation, string Status, string Code = "", decimal Unit = 0, decimal Total = 0, int Remaining = 0);

internal sealed class EventWorkerState(string key, Operation operation)
{
    private bool completed;
    internal string Key { get; } = key;
    internal Operation Operation { get; } = operation;
    internal Outcome Outcome { get; private set; }
    internal WorkerPacket? Packet { get; private set; }
    internal bool ExitConfirmed { get; private set; }
    internal bool Reliable => ExitConfirmed && Packet != null && Outcome is Outcome.Success or Outcome.BusinessRefusal or Outcome.CleanupFailure;
    internal bool Committed => Reliable && Outcome is Outcome.Success or Outcome.CleanupFailure;
    internal bool Strict => Reliable && Outcome != Outcome.CleanupFailure;
    internal static bool ValidTicketCode(string value) => Regex.IsMatch(value, @"\ATKT-[0-9]{8}-[0-9A-F]{12}\z", RegexOptions.CultureInvariant);
    internal static bool Purchasing(Operation op) => op is Operation.Buy or Operation.Seed;
    internal static bool ValidPacket(WorkerPacket p, string key, Operation op)
    {
        if (p.Key != key || p.Operation != op.ToString() || p.Code == null || p.Status == null) return false;
        if (p.Status is "COMMIT_UNCERTAIN" or "ROLLBACK_UNCERTAIN" or "TECHNICAL_ERROR")
            return p.Code.Length == 0 || ValidTicketCode(p.Code);
        bool known = Purchasing(op) ? Enum.GetNames<TicketBookingStatus>().Contains(p.Status, StringComparer.Ordinal)
            : Enum.GetNames<EventUpdateStatus>().Contains(p.Status, StringComparer.Ordinal);
        if (!known) return false;
        bool committed = p.Status is "Success" or "CommittedWithCleanupFailure";
        if (!Purchasing(op)) return p.Code == "" && p.Unit == 0 && p.Total == 0 && p.Remaining == 0;
        return committed ? ValidTicketCode(p.Code) && p.Unit == EventPlan.Price && p.Total == p.Unit * (op == Operation.Seed ? 5 : 2)
            && p.Remaining >= 0 : p.Code == "" && p.Unit == 0 && p.Total == 0 && p.Remaining >= 0;
    }
    internal void Complete(string text, int exitCode, bool stopped)
    {
        if (completed || Outcome != Outcome.Unknown) { Fail(new InvalidOperationException()); return; }
        completed = true; ExitConfirmed = stopped;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
        // Uncertainty dominates malformed or duplicate terminal messages.
        if (lines.Any(l => Regex.IsMatch(l, "\"Status\"\\s*:\\s*\"COMMIT_UNCERTAIN\"")))
        { Outcome = Outcome.CommitUncertain; return; }
        if (lines.Any(l => Regex.IsMatch(l, "\"Status\"\\s*:\\s*\"ROLLBACK_UNCERTAIN\"")))
        { Outcome = Outcome.RollbackUncertain; return; }
        if (lines.Count != 1 || !lines[0].StartsWith("RESULT ", StringComparison.Ordinal))
        { Outcome = lines.Count == 0 ? Outcome.Unknown : Outcome.Invalid; return; }
        try
        {
            using var doc = JsonDocument.Parse(lines[0][7..]);
            var properties = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            Safety.Require(properties.Length == 7 && properties.ToHashSet(StringComparer.Ordinal).SetEquals(
                new[] { "Key", "Operation", "Status", "Code", "Unit", "Total", "Remaining" }));
            Packet = JsonSerializer.Deserialize<WorkerPacket>(lines[0][7..]);
        }
        catch { Outcome = Outcome.Invalid; return; }
        if (Packet == null || !ValidPacket(Packet, Key, Operation)) { Outcome = Outcome.Invalid; return; }
        if (Packet.Status == "TECHNICAL_ERROR") { Outcome = Outcome.TechnicalUnknown; return; }
        if (exitCode != 0 || !stopped) { Outcome = Outcome.Crashed; return; }
        Outcome = Packet.Status switch
        {
            "Success" => Outcome.Success, "CommittedWithCleanupFailure" => Outcome.CleanupFailure, _ => Outcome.BusinessRefusal
        };
    }
    internal void Fail(Exception error)
    {
        if (Outcome is Outcome.CommitUncertain or Outcome.RollbackUncertain) return;
        Outcome = error is TimeoutException ? Outcome.TimedOut : Outcome.TechnicalUnknown;
    }
    internal void Stopped(bool forced)
    {
        ExitConfirmed = true;
        if (forced && Outcome is not (Outcome.CommitUncertain or Outcome.RollbackUncertain)) Outcome = Outcome.Crashed;
    }
    internal void StopUnconfirmed() => ExitConfirmed = false;
    internal static bool CanCleanup(bool created, bool verified, string[] expectedKeys, IReadOnlyList<EventWorkerState> workers) =>
        created && verified && expectedKeys.Length is 2 or 3 &&
        expectedKeys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedKeys.Length == 2 ? new[] { "A", "B" } : new[] { "S", "A", "B" }) && workers.Count == expectedKeys.Length &&
        workers.Select(w => w.Key).Order().SequenceEqual(expectedKeys.Order()) &&
        workers.Select(w => w.Key).Distinct(StringComparer.Ordinal).Count() == workers.Count && workers.All(w => w.Reliable);
}

internal sealed class EventWorker(Process process, string key, Operation operation) : IDisposable
{
    internal EventWorkerState State { get; } = new(key, operation);
    internal static EventWorker Start(EventFixture fixture, string key, Operation operation)
    {
        EventDatabaseGuard.RequireRunner();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { Assembly.GetExecutingAssembly().Location, Program.Execute, "--worker", fixture.Plan.Marker,
            fixture.Id.ToString(CultureInfo.InvariantCulture), fixture.Plan.Case.Name, operation.ToString(), key }) start.ArgumentList.Add(arg);
        foreach (var flag in new[] { "NEST_ALLOW_B21_WRITES", "NEST_ALLOW_B22_WRITES", "NEST_ALLOW_ROOM_CONCURRENCY_WRITES" }) start.Environment.Remove(flag);
        return new EventWorker(Process.Start(start) ?? throw new InvalidOperationException(), key, operation);
    }
    private void processErrorDrain() { process.ErrorDataReceived += (_, _) => { }; process.BeginErrorReadLine(); }
    internal async Task Ready(EventSnapshot baseline)
    {
        try
        {
            processErrorDrain();
            var expected = $"READY {State.Key} {State.Operation} {baseline.Fingerprint} {Safety.RepositoryBuild}";
            Safety.Require(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)) == expected);
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
            var output = await process.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            State.Complete(output, process.ExitCode, process.HasExited);
            Safety.Require(State.Reliable);
        }
        catch (Exception ex) { State.Fail(ex); throw; }
    }
    internal async Task StopOwnWorker()
    {
        try
        {
            bool forced = !process.HasExited;
            if (forced) process.Kill(entireProcessTree: true); // Only this test's own Process handle, never Nest/windows.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Safety.Require(process.HasExited); State.Stopped(forced);
        }
        catch { State.StopUnconfirmed(); throw; }
    }
    internal void Report() => Console.WriteLine($"WORKER key={State.Key}; op={State.Operation}; pid={process.Id}; outcome={State.Outcome}; result={State.Packet?.Status ?? "none"}; code={State.Packet?.Code ?? "none"}; exitConfirmed={State.ExitConfirmed}");
    public void Dispose() => process.Dispose();

    internal static async Task<int> RunChild(string[] args)
    {
        Safety.Require(Program.Worker(args)); EventDatabaseGuard.RequireRunner();
        var plan = EventPlan.Parse(args[2], args[4]);
        Safety.Require(int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 && id.ToString(CultureInfo.InvariantCulture) == args[3]);
        Safety.Require(Enum.GetNames<Operation>().Contains(args[5], StringComparer.Ordinal));
        var operation = Enum.Parse<Operation>(args[5]); var key = args[6];
        Safety.Require(plan.Case.Permitted(key, operation));
        EventSnapshot baseline;
        using (var conn = EventDatabaseGuard.Open())
        {
            EventDatabaseGuard.Schema(conn); baseline = EventFixture.ReadOwned(conn, plan, id);
        }
        var request = baseline.Row.Request(operation); // Captured before GO, deliberately stale in case D.
        Console.WriteLine($"READY {key} {operation} {baseline.Fingerprint} {Safety.RepositoryBuild}");
        Safety.Require(await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90)) == "GO");
        WorkerPacket result;
        try
        {
            using (var check = EventDatabaseGuard.Open()) { EventDatabaseGuard.Schema(check); EventDatabaseGuard.Target(check); }
            EventDatabaseGuard.RequireRunner();
            var repository = new EventRepository();
            if (EventWorkerState.Purchasing(operation))
            {
                var r = repository.BookTickets(new TicketBookingRequest
                {
                    EventId = id, Quantity = operation == Operation.Seed ? 5 : 2,
                    GuestName = plan.Guest(key), GuestEmail = plan.Email(key), GuestPhone = EventPlan.Phone, ReservationId = null
                });
                result = new(key, operation.ToString(), r.Status.ToString(), r.TicketCode, r.UnitPrice, r.TotalPrice, r.RemainingTickets);
            }
            else result = new(key, operation.ToString(), repository.Update(request).Status.ToString());
        }
        catch (EventCommitUncertainException ex) { result = new(key, operation.ToString(), "COMMIT_UNCERTAIN", ex.TicketCode ?? ""); }
        catch (EventRollbackUncertainException) { result = new(key, operation.ToString(), "ROLLBACK_UNCERTAIN"); }
        catch { result = new(key, operation.ToString(), "TECHNICAL_ERROR"); }
        Console.WriteLine("RESULT " + JsonSerializer.Serialize(result));
        return result.Status is "COMMIT_UNCERTAIN" or "ROLLBACK_UNCERTAIN" or "TECHNICAL_ERROR" ? 2 : 0;
    }
}
