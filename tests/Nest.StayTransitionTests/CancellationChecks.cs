using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using StayTests;
using StayTests.Data;

internal static class CancellationChecks
{
    private static readonly Dictionary<string, string> SqlBaseline = JsonSerializer.Deserialize<Dictionary<string, string>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CancellationSqlBaseline.json"))) ?? throw new Exception("Missing cancellation baseline");

    private static readonly (string Scenario, CancellationResult? Result)[] Cases =
    {
        ("normal", CancellationResult.Success),
        ("already-cancelled", CancellationResult.ReservationChangedOrNotFound),
        ("checked-out", CancellationResult.ReservationChangedOrNotFound),
        ("checked-in", CancellationResult.RequiresCheckOut),
        ("missing-reservation", CancellationResult.ReservationChangedOrNotFound),
        ("missing-room", CancellationResult.RoomNotFound), ("room-id-mismatch", CancellationResult.RoomNotFound),
        ("missing-after-candidate", CancellationResult.ReservationChangedOrNotFound),
        ("room-changed", CancellationResult.ReservationChangedOrNotFound),
        ("status-changed", CancellationResult.ReservationChangedOrNotFound),
        ("code-changed", CancellationResult.ReservationChangedOrNotFound),
        ("first-zero", CancellationResult.ReservationChangedOrNotFound),
        ("first-two", CancellationResult.ReservationChangedOrNotFound),
        ("maintenance", CancellationResult.Success), ("occupied", CancellationResult.Success),
        ("deleted", CancellationResult.Success), ("deleted-maintenance", CancellationResult.Success),
        ("other-occupant", CancellationResult.Success),
        ("create", null), ("open", null), ("begin", null), ("candidate", null), ("room-lock", null),
        ("reservation-lock", null), ("first-sql", null), ("first-sql,rollback", null),
        ("first-sql,rollback,transaction-dispose,connection-dispose", null),
        ("first-zero,rollback", null), ("first-zero,transaction-dispose", null),
        ("first-zero,connection-dispose", null), ("first-zero,rollback,transaction-dispose", null),
        ("first-zero,rollback,connection-dispose", null),
        ("first-zero,rollback,transaction-dispose,connection-dispose", null),
        ("commit-before", null), ("commit-before,rollback", null),
        ("commit-before,rollback,transaction-dispose,connection-dispose", null),
        ("commit-after", null), ("commit-after,rollback", null),
        ("transaction-dispose", CancellationResult.CommittedWithCleanupFailure),
        ("connection-dispose", CancellationResult.CommittedWithCleanupFailure),
        ("transaction-dispose,connection-dispose", CancellationResult.CommittedWithCleanupFailure)
    };

    private static void Configure(string scenario)
    {
        State.Fault = scenario;
        if (scenario == "missing-reservation") State.Bookings.Remove("TEST");
        if (scenario == "missing-room") State.RoomExists = false;
        if (scenario is "already-cancelled" or "checked-out" or "checked-in")
            State.Bookings["TEST"] = State.Bookings["TEST"] with { Status = scenario == "checked-in" ? "Eingecheckt" : scenario == "checked-out" ? "Ausgecheckt" : "Storniert" };
        State.Deleted = scenario.StartsWith("deleted", StringComparison.Ordinal);
        if (scenario.Contains("maintenance")) State.RoomStatus = "Wartung";
        if (scenario == "occupied") State.RoomStatus = "Belegt";
        if (scenario == "other-occupant") State.Bookings["OTHER"] = new(2, "OTHER", 1, "Eingecheckt");
    }

    internal static void Run(Action<bool, string> check)
    {
        check(SqlBaseline.Count == 4 && SqlBaseline.Values.Distinct().Count() == 4, "Four independent cancellation SQL baseline statements");
        foreach (var entry in Cases)
        {
            State.Reset("Reserviert", "Verfuegbar"); Configure(entry.Scenario);
            var originalRoom = State.RoomStatus; var originalDeleted = State.Deleted;
            var originalBooking = State.Bookings.GetValueOrDefault("TEST");
            CancellationResult? result = null; Exception? error = null;
            try { result = new ReservationRepository().CancelReservation("TEST"); }
            catch (Exception ex) { error = ex; }
            check(result == entry.Result && (entry.Result.HasValue ? error == null : error != null), "Cancellation result: " + entry.Scenario);
            var committed = result is CancellationResult.Success or CancellationResult.CommittedWithCleanupFailure;
            check(State.Commits == (committed || entry.Scenario.StartsWith("commit-", StringComparison.Ordinal) ? 1 : 0), "Cancellation commits only successful SQL");
            check(State.Rollbacks == (entry.Scenario is "create" or "open" or "begin" || committed ? 0 : 1), "One rollback only before confirmed Commit");
            check(State.RoomStatus == originalRoom && State.Deleted == originalDeleted && State.RoomUpdates == 0, "Cancellation never changes room status or deletion");
            if (originalBooking != null)
                check(State.Bookings["TEST"].Status == (committed || entry.Scenario.StartsWith("commit-after", StringComparison.Ordinal) ? "Storniert" : originalBooking.Status), "Cancellation write/rollback state");
            check(State.Bookings.GetValueOrDefault("OTHER")?.Status is null or "Eingecheckt", "Other reservation untouched");
            check(State.ConnectionsDisposed == (entry.Scenario == "create" ? 0 : 1), "Cancellation connection disposed");
            check(State.TransactionsDisposed == (entry.Scenario is "create" or "open" or "begin" ? 0 : 1), "Cancellation transaction disposed");

            if (entry.Scenario.StartsWith("commit-", StringComparison.Ordinal))
            {
                var commitFault = entry.Scenario.StartsWith("commit-before", StringComparison.Ordinal) ? "commit-before" : "commit-after";
                check(error is ReservationCommitUncertainException && ReferenceEquals(error.InnerException, State.Errors[commitFault]), "Cancellation only direct Commit error uncertain, InnerException retained");
            }
            else check(error is not ReservationCommitUncertainException, "Cancellation pre/post-Commit failures not uncertain");
            if (entry.Scenario.StartsWith("first-sql", StringComparison.Ordinal))
                check(ReferenceEquals(error, State.Errors["first-sql"]), "Cancellation SQL exception not masked by cleanup");
            if (entry.Scenario.StartsWith("first-zero,rollback", StringComparison.Ordinal))
                check(error is ReservationRollbackFailedException && ReferenceEquals(error.InnerException, State.Errors["rollback"]), "Rollback failure stays technical with original cause");
            if (entry.Scenario.Contains("rollback") && !entry.Scenario.StartsWith("first-zero", StringComparison.Ordinal))
                check(ReferenceEquals(error?.Data["RollbackException"], State.Errors["rollback"]), "Secondary rollback error retained separately");
            foreach (var cleanup in new[] { ("transaction-dispose", "TransactionDisposeException"), ("connection-dispose", "ConnectionDisposeException") })
                if (entry.Scenario.Contains(cleanup.Item1) && error != null &&
                    (entry.Scenario.StartsWith("first-sql", StringComparison.Ordinal) || entry.Scenario.StartsWith("commit-", StringComparison.Ordinal) || entry.Scenario.StartsWith("first-zero,rollback", StringComparison.Ordinal)))
                    check(ReferenceEquals(error.Data[cleanup.Item2], State.Errors[cleanup.Item1]), "Cancellation Dispose error retained without replacing primary");

            var commands = State.Commands.ToArray();
            foreach (var command in commands)
            {
                check(SqlBaseline.Values.Contains(command.Sql), "Cancellation SQL byte-for-byte published baseline");
                var keys = System.Text.RegularExpressions.Regex.Matches(command.Sql, @"@\w+").Select(match => match.Value).Distinct().Order();
                check(keys.SequenceEqual(command.Parameters.Keys.Order()), "Cancellation parameters unchanged");
                foreach (var parameter in command.Parameters)
                    check(Equals(parameter.Value, parameter.Key == "@code" ? (object)"TEST" : 1), "Cancellation stable code/RoomId parameter");
            }
            check(!commands.Any(command => command.Sql.Contains("UPDATE zimmer") || command.Sql.Contains("otherOccupants")), "No room update or added business query");
            check(commands.Count(command => command.Sql.Contains("SELECT zimmer_id")) <= 1 && State.Updates <= 1, "Cancellation no automatic retries");
            if (commands.Any(command => command.Sql.Contains("UPDATE reservierungen")))
                check(State.Events.Take(2).SequenceEqual(new[] { "room-locked", "reservation-locked" }), "Cancellation locks room first then reservation");
            if (entry.Scenario == "normal")
                check(new[] { "findRoomSql", "lockRoomSql", "lockReservationSql", "updateSql" }.Select(key => SqlBaseline[key]).SequenceEqual(commands.Select(command => command.Sql)), "Four cancellation SQL statements in original order");
            var released = Monitor.TryEnter(State.RoomGate); check(released, "Cancellation lock released"); if (released) Monitor.Exit(State.RoomGate);
        }

        var uiCases = Cases.Select(entry => entry.Scenario).Concat(new[] { "no-selection", "ui-checked-in", "ui-cancelled", "decline", "reservations-load", "rooms-load", "ui" }).ToArray();
        foreach (var scenario in uiCases)
        {
            var baseline = RunUi(legacy: true, scenario);
            var current = RunUi(legacy: false, scenario);
            check(baseline.Dialogs.SequenceEqual(current.Dialogs), "Exact published cancellation dialogs (text/title/icon/button): " + scenario);
            check(baseline.Notifications.SequenceEqual(current.Notifications), "Cancellation WPF notification order: " + scenario);
            check(baseline.Events.SequenceEqual(current.Events), "Cancellation loading/Commit order: " + scenario);
            check(baseline.Snapshot == current.Snapshot, "Cancellation form, collections, selection, counters match published behavior: " + scenario);
            check(current.Dialogs.All(dialog => !dialog.Text.Contains("PRIVATE")), "Cancellation no exception details in dialogs");
        }

        // Sequential controls for both legal race outcomes; this is NOT a real MariaDB concurrency test.
        State.Reset("Reserviert", "Verfuegbar");
        check(new ReservationRepository().ApplyStayTransition("TEST", StayTransition.CheckIn) == StayTransitionResult.Success, "Control check-in first");
        check(new ReservationRepository().CancelReservation("TEST") == CancellationResult.RequiresCheckOut && State.Bookings["TEST"].Status == "Eingecheckt" && State.RoomStatus == "Belegt", "Control cancellation cannot undo check-in");
        State.Reset("Reserviert", "Verfuegbar");
        check(new ReservationRepository().CancelReservation("TEST") == CancellationResult.Success, "Control cancellation first");
        check(new ReservationRepository().ApplyStayTransition("TEST", StayTransition.CheckIn) == StayTransitionResult.ReservationChanged && State.Bookings["TEST"].Status == "Storniert" && State.RoomStatus == "Verfuegbar", "Control cancelled booking cannot check in");
        Console.WriteLine($"Cancellation regression: {Cases.Length} Repository scenarios and {uiCases.Length} differential WPF scenarios against published ea22b79; fake transport only.");
    }

    private sealed record UiResult((string Text, string Title, MessageBoxButton Button, MessageBoxImage Image)[] Dialogs,
        string?[] Notifications, string[] Events, string Snapshot);

    private static UiResult RunUi(bool legacy, string scenario)
    {
        State.Reset("Reserviert", "Verfuegbar");
        var shell = (Nest.ViewModels.Shell.ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(Nest.ViewModels.Shell.ShellViewModel));
        object vm = legacy ? new StayTests.LegacyUi.ReservationManagementViewModel(shell) : new StayTests.Ui.ReservationManagementViewModel(shell);
        static object? Get(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target);
        static void Set(object target, string name, object? value) => target.GetType().GetProperty(name)?.SetValue(target, value);
        if (vm.GetType().GetMethod("LoadReservationsFromDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.DeclaringType?.Namespace == "StayTests.Ui")
        {
            vm.GetType().GetMethod("LoadRoomsFromDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, new object[] { false });
            vm.GetType().GetMethod("LoadReservationsFromDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm, new object[] { false });
        }
        var reservations = Get(vm, "Reservations") ?? throw new Exception();
        var rooms = Get(vm, "RoomList") ?? throw new Exception();
        var selected = ((IEnumerable)reservations).Cast<object>().Single();
        var selectedRoom = ((IEnumerable)rooms).Cast<object>().Single();
        Set(vm, "SelectedReservation", selected); Set(vm, "SelectedRoom", selectedRoom);
        Set(vm, "FirstName", "Keep"); Set(vm, "LastName", "Form"); Set(vm, "SpecialRequests", "Keep request"); Set(vm, "Deposit", 10m);
        Configure(scenario);
        if (scenario == "no-selection") Set(vm, "SelectedReservation", null);
        if (scenario is "ui-checked-in" or "ui-cancelled") Set(selected, "Status", scenario == "ui-checked-in" ? "Eingecheckt" : "Storniert");
        if (scenario == "decline") Dialog.Answer = MessageBoxResult.No;
        var notifications = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        State.ReservationReads = State.RoomReads = State.Opens = State.Begins = 0; State.Events = new(); Dialog.Messages = new();
        vm.GetType().GetMethod("CancelReservation")?.Invoke(vm, null);
        var resultSelection = Get(vm, "SelectedReservation");
        var snapshot = string.Join("|", new object?[]
        {
            State.Opens, State.Begins, State.Updates, State.RoomUpdates, State.Commits, State.Rollbacks,
            State.ReservationReads, State.RoomReads, State.ConnectionsDisposed, State.TransactionsDisposed,
            ReferenceEquals(Get(vm, "Reservations"), reservations), ReferenceEquals(Get(vm, "RoomList"), rooms),
            ReferenceEquals(resultSelection, selected), ReferenceEquals(Get(vm, "SelectedRoom"), selectedRoom),
            resultSelection == null ? "none" : Get(resultSelection, "ReservationCode"),
            resultSelection == null ? "none" : Get(resultSelection, "Status"),
            Get(vm, "FirstName"), Get(vm, "LastName"), Get(vm, "SpecialRequests"), Get(vm, "Deposit"),
            State.Bookings.GetValueOrDefault("TEST")?.Status, State.RoomStatus, State.Deleted
        });
        return new(Dialog.Messages.ToArray(), notifications.ToArray(), State.Events.ToArray(), snapshot);
    }
}
