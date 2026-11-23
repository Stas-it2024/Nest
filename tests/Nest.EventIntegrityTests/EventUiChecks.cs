using EventTests.Data;
using EventTests.Ui.Admin;
using EventTests.Ui.CustomerPortal;
using Nest.Models;

namespace EventTests;

internal static class EventUiChecks
{
    private static Veranstaltung Card() => new()
    {
        Id = 1, Titel = "Test event", Datum = DateTime.Today.AddDays(5), Uhrzeit = TimeSpan.FromHours(19),
        MaximaleTickets = 10, VerkaufteTickets = 5, TicketPreis = 12.50m
    };
    private static VeranstaltungenViewModel Customer()
        => new(new EventRepository(), "Offline Test") { KundenEmail = "offline@example.invalid", KundenTelefon = "test-only", AnzahlTickets = "2" };
    private static EventViewModel Admin()
    {
        var vm = new EventViewModel(); vm.SelectedEvent = vm.Events.Single();
        vm.Title = "Unsaved title"; vm.Description = "Unsaved description";
        return vm;
    }
    internal static void Run()
    {
        State.Reset(new Row()); var admin = Admin();
        State.Row = State.Row! with { Sold = 7 }; admin.MaxTicketsText = "6";
        var selection = admin.SelectedEvent;
        admin.Aktualisieren();
        Program.Check(admin.Title == "Unsaved title" && admin.Description == "Unsaved description"
            && admin.MaxTicketsText == "6" && ReferenceEquals(selection, admin.SelectedEvent), "admin draft and selection preserved on refusal");
        Program.Check(State.Writes == 0 && Dialog.Messages.Any(m => m.Contains("(7)")), "admin refusal uses locked counter");
        admin.MaxTicketsText = "7"; admin.Aktualisieren();
        Program.Check(admin.SelectedEvent?.SoldTickets == 7 && admin.Events.Single().SoldTickets == 7, "admin uses returned current row");

        foreach (bool afterCommit in new[] { false, true })
        {
            State.Reset(new Row()); var vm = Customer();
            State.Faults.Add(afterCommit ? "commit-after" : "commit");
            vm.TicketKaufen(Card()); int writes = State.Writes;
            Program.Check(vm.Statusmeldung.Contains("ungeklärt") && !vm.Statusmeldung.Contains("zurückgesetzt"), "unknown commit message is honest");
            Program.Check(!vm.CanTicketKaufen(Card()), "unknown purchase disables repeat");
            vm.TicketKaufen(Card());
            Program.Check(State.Writes == writes && State.Commits == 1, "direct call cannot repeat unknown purchase");
        }
        State.Reset(new Row()); var rollbackVm = Customer(); State.Faults.UnionWith(new[] { "sale", "rollback" });
        rollbackVm.TicketKaufen(Card());
        Program.Check(rollbackVm.Statusmeldung.Contains("nicht bestätigt") && !rollbackVm.CanTicketKaufen(Card()), "rollback unknown UI");
        State.Reset(new Row()); var failed = Customer(); State.Faults.Add("sale"); failed.TicketKaufen(Card());
        Program.Check(failed.Statusmeldung.Contains("zurückgesetzt") && State.Orders == 0, "confirmed rollback message");
        State.Reset(new Row()); var cleanupVm = Customer(); State.Faults.Add("connection-dispose"); cleanupVm.TicketKaufen(Card());
        Program.Check(cleanupVm.Statusmeldung.Contains("gespeichert") && !cleanupVm.Statusmeldung.Contains("nicht gespeichert")
            && State.Orders == 1, "confirmed cleanup failure not called failed purchase");

        State.Reset(new Row()); var refreshVm = Customer(); State.Faults.Add("list-read"); refreshVm.TicketKaufen(Card());
        Program.Check(State.Orders == 1 && refreshVm.Statusmeldung.Contains("erfolgreich"), "list refresh failure cannot undo confirmed success");
        Program.Check(!Dialog.Messages.Any(m => m.Contains("Alle Änderungen wurden zurückgesetzt")), "no false guaranteed rollback");

        State.Reset(new Row()); var brokenUi = Customer();
        brokenUi.Veranstaltungen.CollectionChanged += (_, _) => throw new InvalidOperationException("synthetic UI refresh failure");
        brokenUi.TicketKaufen(Card());
        Program.Check(State.Orders == 1 && brokenUi.Statusmeldung.Contains("wurde gespeichert")
            && brokenUi.Statusmeldung.Contains("Anzeige"), "exception in UI refresh after commit is not a failed purchase");

        State.Reset(new Row()); var staleVm = Customer(); State.Row = State.Row! with { Status = "Inaktiv" };
        staleVm.TicketKaufen(Card());
        Program.Check(State.Writes == 0 && Dialog.Messages.Any(m => m.Contains("nicht für den Ticketverkauf aktiv")), "stale active card rejected");
        State.Reset(new Row()); var unknownAdmin = Admin(); State.Faults.Add("commit-after"); unknownAdmin.Aktualisieren();
        int prior = State.Writes; unknownAdmin.Aktualisieren();
        Program.Check(State.Writes == prior && unknownAdmin.Title == "Unsaved title", "admin unknown result no repeat or draft reset");
        State.Reset(new Row()); var cleanupAdmin = Admin(); State.Faults.Add("transaction-dispose"); cleanupAdmin.Aktualisieren();
        Program.Check(Dialog.Messages.Any(m => m.Contains("Änderung wurde gespeichert")), "admin committed cleanup message");
    }
}
