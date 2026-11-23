using EventTests.Data;
using Nest.Models;
using System.Reflection;
using System.Windows;

namespace EventTests;

internal static class Program
{
    private static int checks;
    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }
    internal static TicketBookingRequest Buy(int quantity = 2) => new()
    {
        EventId = 1, Quantity = quantity, GuestName = "Offline Test", GuestEmail = "offline@example.invalid", GuestPhone = "test-only"
    };
    internal static EventUpdateRequest Edit(int capacity = 10, string status = "Aktiv", string title = "Renamed")
        => new(1, title, null, DateTime.Today.AddDays(5), TimeSpan.FromHours(19), "Lobby", 12.50m, capacity, null, status);
    internal static Exception Capture(Action action)
    {
        try { action(); } catch (Exception ex) { return ex; }
        throw new Exception("Expected exception");
    }
    private static void Closed() => Check(State.Connections == State.Closed, "all created connections disposed");

    [STAThread]
    public static int Main()
    {
        try
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            RepositoryChecks();
            EventUiChecks.Run();
            Console.WriteLine($"PASS: {checks} EventIntegrity offline checks (production bodies, fake transport; no MariaDB).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void RepositoryChecks()
    {
        var repo = new EventRepository();
        Check(typeof(EventUpdateRequest).GetProperty("SoldTickets") == null, "request has no sales");
        Check(typeof(EventRepository).GetMethods().Where(m => m.Name == "Update")
            .All(m => m.GetParameters().Single().ParameterType == typeof(EventUpdateRequest)), "no old public Update(Event)");

        State.Reset(new Row());
        var stale = repo.GetAll().Single();
        Check(stale.SoldTickets == 5, "admin read 5");
        Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.Success, "buy 2");
        var saved = repo.Update(Edit());
        Check(saved.Status == EventUpdateStatus.Success && saved.Current?.SoldTickets == 7 && State.Row?.Sold == 7, "5 -> 7 -> admin -> 7");
        Check(State.Orders == 1 && State.Quantity == 2 && State.Row?.Author == 9, "history and author preserved");
        Check(!State.Sql.Single(s => s.Contains("SET titel")).Contains("verkaufte_tickets"), "admin SQL cannot write sales");
        Check(State.Trace.IndexOf("lock") < State.Trace.IndexOf("insert"), "purchase locks first");
        Closed();

        foreach (int max in new[] { -1, 0, 4, 5, 6, 20 })
        {
            State.Reset(new Row());
            var r = repo.Update(Edit(max));
            Check(r.Status == (max <= 0 ? EventUpdateStatus.InvalidCapacity : max < 5
                ? EventUpdateStatus.CapacityBelowSold : EventUpdateStatus.Success), "capacity " + max);
            Check(State.Row?.Sold == 5, "capacity edit preserves sales");
            Check(State.Commits == (max >= 5 ? 1 : 0), "commit only valid capacity"); Closed();
        }
        State.Reset(new Row());
        Check(repo.Update(Edit(status: "unknown")).Status == EventUpdateStatus.InvalidStatus, "invalid administrative status");
        Check(State.Writes == 0, "no invalid status write");

        // Explicit serialization orders, no random delays. These are model tests, not server lock evidence.
        State.Reset(new Row()); repo.BookTickets(Buy());
        Check(repo.Update(Edit(6)).Status == EventUpdateStatus.CapacityBelowSold && State.Row?.Max == 10, "purchase before shrink");
        State.Reset(new Row()); repo.Update(Edit(6));
        Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.InsufficientTickets && State.Orders == 0, "shrink before purchase");
        State.Reset(new Row()); repo.BookTickets(Buy()); repo.Update(Edit(status: "Inaktiv"));
        Check(State.Row?.Status == "Inaktiv" && State.Row.Sold == 7 && State.Orders == 1, "buy before deactivate");
        State.Reset(new Row()); repo.Update(Edit(status: "Inaktiv"));
        Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.EventNotActive && State.Orders == 0, "deactivate before buy");

        State.Reset(new Row(Sold: 9));
        using (var start = new Barrier(2))
        {
            Task<TicketBookingResult> Worker() => Task.Run(() =>
            {
                if (!start.SignalAndWait(TimeSpan.FromSeconds(5))) throw new Exception("Start timeout");
                return new EventRepository().BookTickets(Buy(1));
            });
            var a = Worker(); var b = Worker();
            Check(Task.WaitAll(new Task[] { a, b }, TimeSpan.FromSeconds(10)), "model workers finished");
            Check(new[] { a.Result.Status, b.Result.Status }.Count(s => s == TicketBookingStatus.Success) == 1, "one last-ticket success");
            Check(new[] { a.Result.Status, b.Result.Status }.Count(s => s == TicketBookingStatus.InsufficientTickets) == 1, "one last-ticket refusal");
        }
        Check(State.Row?.Sold == 10 && State.Orders == 1 && State.Connections == 2, "last-ticket invariant and independent connections"); Closed();

        State.Reset(null);
        Check(repo.Update(Edit()).Status == EventUpdateStatus.NotFound, "admin missing");
        Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.EventNotFound, "buy missing");
        foreach (string status in new[] { "Inaktiv", "Ausgebucht", "invalid" })
        {
            State.Reset(new Row(Status: status));
            Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.EventNotActive, "inactive " + status);
            Check(repo.GetUpcomingEvents().Count == 0, "inactive list filtered");
            Check(State.Writes == 0, "inactive no writes");
        }
        State.Reset(new Row(Date: DateTime.Today.AddDays(-1)));
        Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.EventInPast, "past");
        foreach (int qty in new[] { -1, 0, int.MaxValue })
        {
            State.Reset(new Row());
            Check(repo.BookTickets(Buy(qty)).Status == (qty <= 0 ? TicketBookingStatus.InvalidQuantity : TicketBookingStatus.InsufficientTickets), "quantity " + qty);
            Check(State.Writes == 0, "invalid qty no writes");
        }
        foreach (var row in new[] { new Row(Sold: -1), new Row(Max: null), new Row(Max: 0), new Row(Sold: 11) })
        {
            State.Reset(row);
            Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.InvalidInventory, "invalid purchase inventory");
            Check(repo.Update(Edit()).Status == EventUpdateStatus.InvalidInventory, "invalid admin inventory");
            Check(State.Writes == 0 && State.Row == row, "no inventory repair");
        }
        State.Reset(new Row(Sold: null));
        Check(repo.GetAll().Single().SoldTickets == 0 && repo.GetUpcomingEvents().Single().VerkaufteTickets == 0, "nullable sales can be loaded in both UIs");
        Check(repo.Update(Edit()).Current?.SoldTickets == 0 && State.Row?.Sold == null, "nullable sales projected zero, not repaired by admin");
        Check(repo.BookTickets(Buy()).Status == TicketBookingStatus.Success && State.Row?.Sold == 2, "nullable sales COALESCE purchase");

        foreach (string kind in new[] { "admin", "insert", "sale" })
        foreach (int affected in new[] { 0, 1, 2 })
        {
            State.Reset(new Row()); State.Affected[kind] = affected;
            Action action = kind == "admin" ? () => repo.Update(Edit()) : () => repo.BookTickets(Buy());
            if (affected == 1) action();
            else
            {
                var ex = Capture(action);
                Check(ex.Data["RollbackConfirmed"] is true, "affected rows rollback " + kind);
                Check(State.Row?.Sold == 5 && State.Orders == 0 && State.Row.Title == "Test event", "no partial writes " + kind);
            }
            Check(State.Commits == (affected == 1 ? 1 : 0), "affected rows commit " + kind + affected); Closed();
        }
        State.Reset(new Row());
        Check(repo.Update(Edit(title: "Test event")).Status == EventUpdateStatus.Success, "matched no-op update success");

        foreach (string kind in new[] { "admin", "insert", "sale", "read" })
        foreach (bool badRollback in new[] { false, true })
        {
            State.Reset(new Row()); State.Faults.Add(kind);
            State.Faults.Add("command-dispose-after-error");
            if (badRollback) State.Faults.Add("rollback");
            var ex = Capture(() => { if (kind == "admin") repo.Update(Edit()); else repo.BookTickets(Buy()); });
            Check(badRollback ? ex is EventRollbackUncertainException && ReferenceEquals(ex.InnerException, State.Primary)
                : ReferenceEquals(ex, State.Primary), "original SQL exception retained " + kind);
            Check(State.Primary.Data.Contains("ResourceDisposeException"), "secondary resource error retained");
            Check(State.Row?.Sold == 5 && State.Orders == 0 && State.Commits == 0, "SQL failure no commit"); Closed();
        }
        foreach (bool admin in new[] { false, true })
        foreach (string fault in new[] { "commit", "commit-after" })
        foreach (bool badRollback in new[] { false, true })
        {
            State.Reset(new Row()); State.Faults.Add(fault);
            State.Faults.Add("transaction-dispose"); State.Faults.Add("connection-dispose");
            if (badRollback) State.Faults.Add("rollback");
            var ex = Capture(() => { if (admin) repo.Update(Edit()); else repo.BookTickets(Buy()); });
            Check(ex is EventCommitUncertainException, "Commit remains uncertain despite rollback/cleanup");
            Check(ex.InnerException?.Message == fault && ex.Data.Contains("TransactionDisposeException") && ex.Data.Contains("ConnectionDisposeException"), "commit original and cleanup retained");
            Check(admin || !string.IsNullOrEmpty(((EventCommitUncertainException)ex).TicketCode), "unknown purchase reference retained");
            Check(State.Commits == 1 && State.Writes == (admin ? 1 : 2), "no automatic write repeat");
            Check(State.Row?.Sold == (!admin && fault == "commit-after" ? 7 : 5), "commit may already be persisted"); Closed();
        }
        foreach (string fault in new[] { "transaction-dispose", "connection-dispose" })
        {
            State.Reset(new Row()); State.Faults.Add(fault);
            var r = repo.BookTickets(Buy());
            Check(r.Status == TicketBookingStatus.CommittedWithCleanupFailure && r.TicketCode.Length == 25 && State.Row?.Sold == 7, "confirmed purchase with cleanup failure"); Closed();
            State.Reset(new Row()); State.Faults.Add(fault);
            var a = repo.Update(Edit());
            Check(a.Status == EventUpdateStatus.CommittedWithCleanupFailure && a.Current?.Title == "Renamed", "confirmed admin with cleanup failure"); Closed();
        }
        State.Reset(null); State.Faults.Add("rollback");
        Check(Capture(() => repo.BookTickets(Buy())) is EventRollbackUncertainException, "business refusal requires confirmed rollback");
        State.Reset(new Row()); State.Faults.Add("reader-dispose");
        Capture(() => repo.BookTickets(Buy()));
        Check(State.Writes == 0 && State.Commits == 0, "reader cleanup failure before writes");
        State.Reset(new Row());
        repo.Add(new Event { Title = "New", Status = "Aktiv", SoldTickets = 999, MaxTickets = 10 });
        Check(State.Trace.Contains("add"), "repository fixes initial sales to zero"); Closed();
        Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a => a.Name is "Nest" or "MySql.Data"), "no production DB transport reachable");
    }
}
