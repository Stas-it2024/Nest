using System.Runtime.CompilerServices;
using System.IO;
using System.Text.Json;
using System.Windows;
using StayTests;
using StayTests.Data;
using StayTests.Ui;

internal static class Program
{
    private static int checks;
    private static readonly string[] Scenarios =
    {
        "normal", "reserved", "occupied", "maintenance", "deleted", "deleted-maintenance", "deleted-free", "deleted-reserved",
        "free", "other-occupant", "missing-room", "missing-reservation", "missing-after-candidate", "room-changed", "status-changed", "code-changed", "already",
        "first-zero", "first-two", "second-zero", "second-two", "create", "open", "begin", "candidate", "room-lock", "reservation-lock", "occupancy",
        "first-sql", "second-sql", "second-sql,rollback", "second-sql,rollback,transaction-dispose,connection-dispose",
        "first-zero,rollback", "first-zero,transaction-dispose", "first-zero,connection-dispose",
        "commit-before", "commit-before,rollback", "commit-after", "transaction-dispose", "connection-dispose"
    };
    private static readonly Dictionary<string, string> SqlBaseline = JsonSerializer.Deserialize<Dictionary<string, string>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StaySqlBaseline.json"))) ?? throw new Exception("Missing baseline");
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }
    private static void Prepare(bool checkIn, string scenario)
    {
        var room = scenario switch
        {
            "reserved" or "deleted-reserved" => "Reserviert", "occupied" => "Belegt", "maintenance" or "deleted-maintenance" => "Wartung",
            "free" or "deleted-free" => "Verfuegbar", _ => checkIn ? "Verfuegbar" : "Belegt"
        };
        State.Reset(checkIn ? "Reserviert" : "Eingecheckt", room);
        State.Deleted = scenario.StartsWith("deleted", StringComparison.Ordinal);
    }
    private static void Configure(bool checkIn, string scenario)
    {
        State.Fault = scenario;
        if (scenario == "missing-room") State.RoomExists = false;
        if (scenario == "missing-reservation") State.Bookings.Remove("TEST");
        if (scenario == "already") State.Bookings["TEST"] = State.Bookings["TEST"] with { Status = checkIn ? "Eingecheckt" : "Ausgecheckt" };
        if (scenario == "other-occupant") State.Bookings["OTHER"] = new(2, "OTHER", 1, "Eingecheckt");
    }
    private static StayTransitionResult? Expected(bool checkIn, string scenario)
    {
        if (scenario is "missing-reservation" or "missing-after-candidate" or "room-changed" or "status-changed" or "code-changed" or "already") return StayTransitionResult.ReservationChanged;
        if (scenario == "missing-room") return StayTransitionResult.UnsuitableRoom;
        if (scenario == "other-occupant") return StayTransitionResult.OccupancyConflict;
        if (scenario is "first-zero" or "first-two" or "second-zero" or "second-two") return StayTransitionResult.UpdateRefused;
        if (scenario is "transaction-dispose" or "connection-dispose") return StayTransitionResult.CommittedWithCleanupFailure;
        if (scenario.StartsWith("commit-", StringComparison.Ordinal) || scenario.Contains(',') ||
            scenario is "create" or "open" or "begin" or "candidate" or "room-lock" or "reservation-lock" or "occupancy" or "first-sql" or "second-sql") return null;
        var allowed = checkIn ? !State.Deleted && State.RoomStatus is "Verfuegbar" or "Reserviert" : State.RoomStatus is "Belegt" or "Wartung";
        return allowed ? StayTransitionResult.Success : StayTransitionResult.UnsuitableRoom;
    }
    private static void CheckSql(bool checkIn, string originalRoom)
    {
        foreach (var command in State.Commands.Where(c => !c.Sql.Contains("CONCAT(g.vorname")))
        {
            Check(SqlBaseline.Values.Contains(command.Sql), "SQL matches pre-refactoring baseline exactly");
            var expectedKeys = System.Text.RegularExpressions.Regex.Matches(command.Sql, @"@\w+")
                .Select(match => match.Value).Distinct().Order().ToArray();
            Check(expectedKeys.SequenceEqual(command.Parameters.Keys.Order()), "All SQL parameters supplied, no extra parameters");
            foreach (var parameter in command.Parameters)
            {
                object expected = parameter.Key switch
                {
                    "@code" => "TEST",
                    "@RoomId" or "@ReservationId" => 1,
                    "@SourceStatus" => checkIn ? "Reserviert" : "Eingecheckt",
                    "@TargetStatus" => checkIn ? "Eingecheckt" : "Ausgecheckt",
                    "@SourceRoomStatus" => originalRoom,
                    "@TargetRoomStatus" => checkIn ? "Belegt" : "Verfuegbar",
                    _ => throw new Exception("Unexpected SQL parameter")
                };
                Check(Equals(parameter.Value, expected), "Canonical ID, code and status parameter values preserved");
            }
        }
        Check(State.Commands.Count(c => c.Sql.Contains("SELECT zimmer_id")) <= 1, "No automatic retry");
        if (State.Commands.Any(c => c.Sql.Contains("UPDATE reservierungen")))
            Check(State.Events.Take(3).SequenceEqual(new[] { "room-locked", "reservation-locked", "occupancy-read" }), "Room-first order");
    }
    [STAThread]
    private static void Main()
    {
        var oldMode = Environment.GetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable);
        var oldConnection = Environment.GetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable);
        try
        {
            // Fail closed for the entire run if any test accidentally reaches the real DB factory.
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable, "invalid-offline-test");
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable, null);
            Check(SqlBaseline.Count == 6 && SqlBaseline.Values.Distinct().Count() == 6, "Exactly six independent SQL baseline statements");
            RepositoryMatrix();
            UiMatrix();
            ConcurrentModel();
            State.Reset("Reserviert", "Verfuegbar");
            try { new ReservationRepository().ApplyStayTransition("TEST", (StayTransition)99); throw new Exception("Expected enum rejection"); }
            catch (ArgumentOutOfRangeException) { Check(State.Opens == 0 && State.Begins == 0, "Invalid direction rejected before connection"); }
            try { new Nest.Data.ReservationRepository().ApplyStayTransition("TEST", (Nest.Data.StayTransition)99); throw new Exception("Expected public API guard"); }
            catch (ArgumentOutOfRangeException) { Check(true, "Actual compiled public API rejects invalid direction before DB access"); }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable, oldMode);
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable, oldConnection);
        }
        Console.WriteLine($"PASS: {checks} assertions; {Scenarios.Length * 2} Repository scenarios, {(Scenarios.Length + 5) * 2} WPF ViewModel scenarios and concurrent lock model. Current production bodies, fake transport/dialogs; NO MariaDB connection.");
    }
    private static void RepositoryMatrix()
    {
        foreach (var checkIn in new[] { true, false })
        foreach (var scenario in Scenarios)
        {
            Prepare(checkIn, scenario); Configure(checkIn, scenario);
            var expected = Expected(checkIn, scenario);
            var originalRoom = State.RoomStatus;
            StayTransitionResult? result = null; Exception? error = null;
            try { result = new ReservationRepository().ApplyStayTransition("TEST", checkIn ? StayTransition.CheckIn : StayTransition.CheckOut); }
            catch (Exception ex) { error = ex; }
            Check(result == expected && (expected.HasValue ? error == null : error != null), $"API result: {checkIn}/{scenario}");
            var committed = result is StayTransitionResult.Success or StayTransitionResult.CommittedWithCleanupFailure;
            Check(State.Commits == (committed || scenario.StartsWith("commit-", StringComparison.Ordinal) ? 1 : 0), "Commit only after successful SQL");
            Check(State.Rollbacks <= 1 && (!committed || State.Rollbacks == 0), "No rollback after confirmed commit");
            if (committed)
            {
                Check(State.Bookings["TEST"].Status == (checkIn ? "Eingecheckt" : "Ausgecheckt"), "Reservation transition");
                var preserve = !checkIn && (State.Deleted || originalRoom == "Wartung");
                Check(State.RoomStatus == (preserve ? originalRoom : checkIn ? "Belegt" : "Verfuegbar"), "Room transition/preservation");
                Check(State.RoomUpdates == (preserve ? 0 : 1), "No UPDATE for preserved room");
            }
            if (scenario.StartsWith("commit-", StringComparison.Ordinal))
                Check(error is ReservationCommitUncertainException && ReferenceEquals(error.InnerException, State.Errors[scenario.StartsWith("commit-before", StringComparison.Ordinal) ? "commit-before" : "commit-after"]), "Only Commit exception becomes uncertain, retaining original");
            else Check(error is not ReservationCommitUncertainException, "Pre/post-commit failures not uncertain");
            if (scenario.StartsWith("second-sql", StringComparison.Ordinal))
            {
                Check(ReferenceEquals(error, State.Errors["second-sql"]), "Primary exception retained");
                if (scenario.Contains("rollback")) Check(ReferenceEquals(error?.Data["RollbackException"], State.Errors["rollback"]), "Rollback exception retained separately");
                if (scenario.Contains("transaction-dispose")) Check(ReferenceEquals(error?.Data["TransactionDisposeException"], State.Errors["transaction-dispose"]), "Transaction cleanup cannot mask primary");
                if (scenario.Contains("connection-dispose")) Check(ReferenceEquals(error?.Data["ConnectionDisposeException"], State.Errors["connection-dispose"]), "Connection cleanup cannot mask primary");
            }
            if (!committed && !scenario.StartsWith("commit-after", StringComparison.Ordinal))
            {
                Check(State.RoomStatus == originalRoom, "No room mutation on refusal/error");
                if (State.Bookings.TryGetValue("TEST", out var row)) Check(row.Status == (scenario == "already" ? checkIn ? "Eingecheckt" : "Ausgecheckt" : checkIn ? "Reserviert" : "Eingecheckt"), "No partial reservation write");
            }
            Check(State.ConnectionsDisposed == (scenario == "create" ? 0 : 1), "Connection disposed");
            Check(State.TransactionsDisposed == (scenario is "create" or "open" or "begin" ? 0 : 1), "Transaction disposed");
            Check(State.Deleted == scenario.StartsWith("deleted", StringComparison.Ordinal), "Deletion unchanged");
            CheckSql(checkIn, originalRoom);
            if (scenario == "normal")
                Check(new[] { "findRoomSql", "lockRoomSql", "lockReservationSql", "otherOccupantsSql", "updateReservationSql", "updateRoomSql" }
                    .Select(key => SqlBaseline[key]).SequenceEqual(State.Commands.Select(command => command.Sql)), "All six baseline statements execute in original order");
            var released = Monitor.TryEnter(State.RoomGate); Check(released, "Lock released"); if (released) Monitor.Exit(State.RoomGate);
        }
    }
    private static void UiMatrix()
    {
        foreach (var checkIn in new[] { true, false })
        foreach (var scenario in Scenarios.Concat(new[] { "no-selection", "wrong-ui-status", "reservations-load", "rooms-load", "ui" }))
        {
            Prepare(checkIn, scenario);
            var shell = (Nest.ViewModels.Shell.ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(Nest.ViewModels.Shell.ShellViewModel));
            var vm = new ReservationManagementViewModel(shell);
            vm.SelectedReservation = vm.Reservations.Single(); vm.SelectedRoom = vm.RoomList.FirstOrDefault();
            vm.FirstName = "Keep"; vm.LastName = "Form"; vm.SpecialRequests = "Keep request"; vm.Deposit = 10;
            var selected = vm.SelectedReservation; var selectedRoom = vm.SelectedRoom; var reservations = vm.Reservations; var rooms = vm.RoomList;
            var notifications = new List<string?>(); vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            Configure(checkIn, scenario);
            if (scenario == "no-selection") vm.SelectedReservation = null;
            if (scenario is "already" or "wrong-ui-status") selected.Status = "Storniert";
            State.ReservationReads = State.RoomReads = 0; State.Events = new(); Dialog.Messages = new();
            var expected = Expected(checkIn, scenario); var operation = checkIn ? "Check-in" : "Check-out";
            if (checkIn) vm.CheckIn(); else vm.CheckOut();
            Check(Dialog.Messages.Count == 1, "One dialog");
            var dialog = Dialog.Messages.Single();
            var expectedText = expected switch
            {
                StayTransitionResult.ReservationChanged => "Die Reservierung wurde nicht gefunden oder inzwischen geändert. Bitte laden Sie die Daten neu.",
                StayTransitionResult.UnsuitableRoom => "Der Zimmerstatus ist für diesen Vorgang nicht geeignet oder widerspricht der Belegung. Bitte prüfen Sie die Daten.",
                StayTransitionResult.OccupancyConflict => "Für dieses Zimmer ist bereits eine andere Reservierung eingecheckt. Der Vorgang wurde nicht ausgeführt. Bitte prüfen Sie die Belegung.",
                StayTransitionResult.UpdateRefused => "Die Reservierung oder das zugehörige Zimmer konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                StayTransitionResult.CommittedWithCleanupFailure => $"Der {operation} wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                StayTransitionResult.Success => $"{operation} für Test Guest erfolgreich!",
                _ => scenario.StartsWith("commit-", StringComparison.Ordinal)
                    ? $"Das Ergebnis des {operation} konnte nicht bestätigt werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen."
                    : $"Der {operation} konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen."
            };
            var title = expected is StayTransitionResult.Success ? "Erfolg" : expected is null or StayTransitionResult.CommittedWithCleanupFailure ? "Fehler" : "Hinweis";
            var image = title == "Erfolg" ? MessageBoxImage.Information : title == "Fehler" ? MessageBoxImage.Error : MessageBoxImage.Warning;
            if (scenario == "no-selection") { expectedText = "Bitte eine Reservierung auswählen."; title = "Hinweis"; image = MessageBoxImage.Warning; }
            if (scenario is "already" or "wrong-ui-status") { expectedText = checkIn ? "Nur Reservierungen mit Status 'Reserviert' können eingecheckt werden." : "Nur eingecheckte Reservierungen können ausgecheckt werden."; title = "Hinweis"; image = MessageBoxImage.Warning; }
            if (scenario is "reservations-load" or "rooms-load" or "ui") { expectedText = $"Der {operation} wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu."; title = "Hinweis"; image = MessageBoxImage.Warning; }
            Check(dialog.Text == expectedText && dialog.Title == title && dialog.Image == image && dialog.Button == MessageBoxButton.OK, $"Exact baseline dialog: {checkIn}/{scenario}");
            Check(!dialog.Text.Contains("PRIVATE"), "No exception details");
            Check(vm.FirstName == "Keep" && vm.LastName == "Form" && vm.SpecialRequests == "Keep request" && vm.Deposit == 10, "Form retained");
            var refresh = expected == StayTransitionResult.Success && scenario is not "already" and not "wrong-ui-status" and not "no-selection";
            if (refresh)
            {
                Check(State.ReservationReads == 1 && State.RoomReads == 1, "Both loaders run after commit");
                var events = State.Events.ToArray();
                Check(Array.IndexOf(events, "committed") < Array.IndexOf(events, "reservations-read") && Array.IndexOf(events, "reservations-read") < Array.IndexOf(events, "rooms-read"), "Commit, reservations, rooms order");
                if (scenario != "reservations-load")
                {
                    Check(vm.SelectedReservation?.ReservationCode == "TEST" && vm.SelectedReservation.Status == (checkIn ? "Eingecheckt" : "Ausgecheckt"), "Selection restored by code");
                    Check(notifications.IndexOf("Reservations") < notifications.IndexOf("SelectedReservation"), "Reservation notifications unchanged");
                }
                else Check(ReferenceEquals(vm.Reservations, reservations) && ReferenceEquals(vm.SelectedReservation, selected), "Reservation reload failure preserves old state");
                if (scenario != "rooms-load") Check(vm.SelectedRoom?.Id == selectedRoom?.Id && notifications.Contains("RoomList") && notifications.Contains("SelectedRoom"), "Room selection and notifications restored");
                else Check(ReferenceEquals(vm.RoomList, rooms) && ReferenceEquals(vm.SelectedRoom, selectedRoom), "Room reload failure preserves old state");
            }
            else
            {
                Check(State.ReservationReads == 0 && State.RoomReads == 0, "No refresh on error/refusal/cleanup failure");
                Check(ReferenceEquals(vm.Reservations, reservations) && ReferenceEquals(vm.RoomList, rooms), "Old collections retained");
                Check(ReferenceEquals(vm.SelectedReservation, scenario == "no-selection" ? null : selected) && ReferenceEquals(vm.SelectedRoom, selectedRoom), "Selection retained on error");
            }
        }
    }
    private static void ConcurrentModel()
    {
        State.Reset("Reserviert", "Verfuegbar"); State.Bookings["SECOND"] = new(2, "SECOND", 1, "Reserviert"); State.Fault = "concurrent";
        var first = Task.Run(() => new ReservationRepository().ApplyStayTransition("TEST", StayTransition.CheckIn));
        Task<StayTransitionResult>? second = null;
        try
        {
            Check(State.FirstLocked.Wait(10000), "First transaction holds room lock");
            second = Task.Run(() => new ReservationRepository().ApplyStayTransition("SECOND", StayTransition.CheckIn));
            Check(State.SecondAttempt.Wait(10000), "Second transaction contends for same room");
        }
        finally { State.ReleaseFirst.Set(); }
        Check(second != null && Task.WaitAll(new Task[] { first, second }, 15000), "Bounded completion");
        Check(first.Result == StayTransitionResult.Success && second?.Result == StayTransitionResult.OccupancyConflict, "One success, one typed conflict in simulated locking");
        Check(State.Bookings.Values.Count(b => b.Status == "Eingecheckt") == 1 && State.RoomStatus == "Belegt" && State.Commits == 1, "No double occupancy in model");
        State.Fault = "";
        Check(new ReservationRepository().ApplyStayTransition("TEST", StayTransition.CheckIn) == StayTransitionResult.ReservationChanged, "Repeated same-booking transition rejected");
    }
}
