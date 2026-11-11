using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using Nest.Synchronization;
using Nest.ViewModels.Admin;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

internal static partial class Program
{
    private static int roomAssertions;
    private static void RoomCheck(bool value)
    { if (!value) throw new InvalidOperationException("Zimmer polling invariant failed."); roomAssertions++; }
    private static PollingSnapshot RoomData(string status = "Verfuegbar", bool include = true, bool second = false, string booking = "Reserviert")
    {
        var original = Data(booking);
        var rooms = include ? new[] { original.Rooms[0] with { Status = status } } : Array.Empty<RoomSnapshot>();
        if (second) rooms = rooms.Append(original.Rooms[0] with { Id = 2, Number = 102 }).ToArray();
        rooms = rooms.Select(r => r with { OriginalValues = new RoomOriginalValues(r.Id,
            r.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Type, r.Capacity,
            r.Price, r.Status, r.Description, r.ImagePath, r.Added, r.Deleted) }).ToArray();
        return new PollingSnapshot(rooms, original.Bookings);
    }
    private static async Task RoomChecks(Ui ui)
    {
        await RoomDraftChecks(ui);
        await RoomCommandChecks(ui);
        await RoomLifecycleChecks(ui);
        await RoomRepositoryConflictChecks(ui);
        Console.WriteLine($"PASS: {roomAssertions} Zimmer assertions; actual ViewModel/lifecycle/bindings, fake reads/writes/dialogs; no SQL.");
    }
    private static async Task RoomDraftChecks(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var updates = 0; var deletes = 0; var messages = 0; var answer = MessageBoxResult.No;
        var vm = await ui.Call(() => new ZimmerViewModel(() => coordinator,
            update: (_, _) => { updates++; return RoomWriteResult.Success; },
            delete: _ => { deletes++; return RoomWriteResult.Success; },
            showDialog: (_, _, buttons, _) => { messages++; return buttons == MessageBoxButton.YesNo ? answer : MessageBoxResult.OK; }));
        RoomCheck(source.Calls == 0 && vm.Rooms.Count == 0);
        await (await ui.Call(() => ((IActivate)vm).ActivateAsync()));
        (await source.Next()).Result.SetResult(RoomData()); var tick = await clock.Live();
        RoomCheck(vm.Rooms.Count == 1 && vm.SelectedRoom == null && vm.RoomNumber == null);
        var collection = vm.Rooms;
        var grid = await ui.Call(() =>
        {
            var control = new DataGrid { ItemsSource = vm.Rooms, DataContext = vm, IsReadOnly = true };
            BindingOperations.SetBinding(control, Selector.SelectedItemProperty, new Binding("SelectedRoom") { Mode = BindingMode.TwoWay });
            CollectionViewSource.GetDefaultView(vm.Rooms).SortDescriptions.Add(new SortDescription("RoomNumber", ListSortDirection.Descending));
            vm.SelectedRoom = vm.Rooms.Single();
            vm.RoomNumber = 777; vm.PriceText = "89,50"; vm.Capacity = 3;
            vm.SelectedTypeIndex = 2; vm.SelectedStatusIndex = 2; vm.Beschreibung = "Unfinished draft";
            return control;
        });
        void Draft() => RoomCheck(vm.RoomNumber == 777 && vm.PriceText == "89,50" && vm.Capacity == 3 &&
            vm.SelectedTypeIndex == 2 && vm.SelectedStatusIndex == 2 && vm.Beschreibung == "Unfinished draft");
        var row = vm.Rooms[0]; var events = 0;
        vm.Rooms.CollectionChanged += (_, _) => events++;
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(RoomData()); tick = await clock.Live();
        RoomCheck(events == 0 && ReferenceEquals(vm.Rooms[0], row)); Draft();
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(RoomData(booking: "Eingecheckt")); tick = await clock.Live();
        RoomCheck(events == 0); // Booking-only changes must not rebuild Zimmer.
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(RoomData("Belegt", second: true)); tick = await clock.Live();
        RoomCheck(vm.Rooms.Count == 2 && vm.SelectedRoom?.Id == 1 && vm.SelectedRoom.Status == "Belegt"); Draft();
        RoomCheck(vm.RoomConflictWarning.Length > 0 && !vm.CanAktualisieren && !vm.CanLöschen);
        RoomCheck(await ui.Call(() => ReferenceEquals(grid.SelectedItem, vm.SelectedRoom)));
        RoomCheck(await ui.Call(() => CollectionViewSource.GetDefaultView(vm.Rooms).SortDescriptions.Single().Direction == ListSortDirection.Descending));
        await ui.Call(() => { vm.Aktualisieren(); return true; });
        RoomCheck(updates == 0 && messages == 1); Draft();
        (await source.Next()).Result.SetResult(RoomData("Belegt", second: true)); await clock.Live();
        await ui.Call(() => { vm.ReloadSelectedRoom(); return true; });
        Draft(); RoomCheck(!vm.CanAktualisieren);
        (await source.Next()).Result.SetResult(RoomData("Belegt", second: true)); await clock.Live();
        answer = MessageBoxResult.Yes;
        await ui.Call(() => { vm.ReloadSelectedRoom(); return true; });
        RoomCheck(vm.RoomNumber == 101 && vm.Beschreibung == null && vm.SelectedStatusIndex == 1 && vm.CanAktualisieren);
        (await source.Next()).Result.SetResult(RoomData("Belegt", second: true)); tick = await clock.Live();
        await ui.Call(() => { vm.PriceText = "91,00"; vm.Beschreibung = "Keep again"; return true; });
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(RoomData(second: true)); tick = await clock.Live();
        RoomCheck(vm.SelectedRoom?.Status == "Verfuegbar" && vm.PriceText == "91,00" && vm.SelectedStatusIndex == 1);
        RoomCheck(vm.RoomConflictWarning.Length > 0 && !vm.CanAktualisieren);
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(RoomData(include: false, second: true)); tick = await clock.Live();
        RoomCheck(vm.Rooms.Count == 1 && vm.SelectedRoom == null && vm.RoomNumber == 101 && vm.PriceText == "91,00" && vm.Beschreibung == "Keep again");
        RoomCheck(vm.RoomConflictWarning.Contains("entfernt") && !vm.CanAktualisieren && !vm.CanLöschen && !vm.CanReloadSelectedRoom);
        await ui.Call(() => { vm.Aktualisieren(); return true; });
        (await source.Next()).Result.SetResult(RoomData(include: false, second: true)); await clock.Live();
        await ui.Call(() => { vm.Löschen(); return true; });
        RoomCheck(updates == 0 && deletes == 0);
        (await source.Next()).Result.SetResult(RoomData(include: false, second: true)); tick = await clock.Live();
        var retained = vm.Rooms[0];
        tick.Done.SetResult(); (await source.Next()).Result.SetException(new InvalidOperationException("offline")); tick = await clock.Live();
        RoomCheck(ReferenceEquals(vm.Rooms[0], retained) && vm.SynchronizationStatus.Contains("Keine Datenbankverbindung"));
        RoomCheck(messages == 5); // Background failure produced no dialog.
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(RoomData(include: false, second: true)); await clock.Live();
        RoomCheck(vm.SynchronizationStatus.Contains("Daten aktuell"));
        await ui.Call(() => { vm.ClearForm(); return true; });
        RoomCheck(vm.SelectedRoom == null && vm.RoomNumber == null && vm.Beschreibung == "" && vm.RoomConflictWarning == "");
        (await source.Next()).Result.SetResult(RoomData(include: false, second: true)); await clock.Live();
        RoomCheck(vm.SelectedRoom == null && ReferenceEquals(collection, vm.Rooms));
        await ui.Call(() => { BindingOperations.ClearAllBindings(grid); return true; });
        await (await ui.Call(() => ((IDeactivate)vm).DeactivateAsync(true)));
    }

    private static async Task RoomCommandChecks(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var adds = 0; var updates = 0; var deletes = 0;
        var result = RoomWriteResult.TechnicalFailure;
        Request? modalRead = null; var answer = MessageBoxResult.Yes; var modalObserved = false;
        var vm = await ui.Call(() => new ZimmerViewModel(() => coordinator,
            add: _ => { adds++; return result; }, update: (_, _) => { updates++; return result; }, delete: _ => { deletes++; return result; },
            showDialog: (_, _, buttons, _) =>
            {
                if (buttons == MessageBoxButton.YesNo && modalRead != null)
                {
                    RoomCheck(modalRead.Token.IsCancellationRequested);
                    modalRead.Result.SetResult(RoomData("Belegt")); modalRead = null;
                    // Pump the UI exactly as a modal dialog does, without a real window.
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new System.Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame); modalObserved = true;
                }
                return buttons == MessageBoxButton.YesNo ? answer : MessageBoxResult.OK;
            }));
        await (await ui.Call(() => ((IActivate)vm).ActivateAsync()));
        (await source.Next()).Result.SetResult(RoomData()); var tick = await clock.Live();
        await ui.Call(() => { vm.SelectedRoom = vm.Rooms.Single(); vm.PriceText = "81,00"; vm.Beschreibung = "Keep command draft"; return true; });
        tick.Done.SetResult(); modalRead = await source.Next();
        await ui.Call(() => { vm.Löschen(); return true; }); // Failure; old in-flight result cannot change selection in confirmation.
        RoomCheck(modalObserved && deletes == 1 && vm.SelectedRoom?.Id == 1 && vm.PriceText == "81,00" && vm.RoomConflictWarning == "");
        (await source.Next()).Result.SetResult(RoomData()); await clock.Live();
        foreach (var failure in new[] { RoomWriteResult.UncertainOutcome, RoomWriteResult.CommitUncertain, RoomWriteResult.Occupied, RoomWriteResult.NumberConflict })
        {
            result = failure;
            await ui.Call(() => { vm.Aktualisieren(); return true; });
            RoomCheck(vm.PriceText == "81,00" && vm.Beschreibung == "Keep command draft" && vm.SelectedRoom?.Id == 1);
            (await source.Next()).Result.SetResult(RoomData()); await clock.Live();
        }
        RoomCheck(updates == 4); // Never retry uncertain writes.
        result = RoomWriteResult.Success;
        await ui.Call(() => { vm.Aktualisieren(); return true; });
        RoomCheck(updates == 5 && vm.RoomNumber == null && vm.SelectedRoom == null);
        (await source.Next()).Result.SetResult(RoomData()); await clock.Live();
        await ui.Call(() => { vm.RoomNumber = 444; vm.PriceText = "55,00"; vm.Beschreibung = "New own room"; vm.Hinzufügen(); return true; });
        RoomCheck(adds == 1 && vm.RoomNumber == null);
        (await source.Next()).Result.SetResult(RoomData(second: true)); await clock.Live();
        await ui.Call(() => { vm.SelectedRoom = vm.Rooms.First(r => r.Id == 2); vm.Löschen(); return true; });
        RoomCheck(deletes == 2);
        (await source.Next()).Result.SetResult(RoomData()); await clock.Live();
        RoomCheck(vm.SelectedRoom == null && vm.RoomConflictWarning.Contains("entfernt"));
        RoomCheck(source.Maximum == 1);
        await (await ui.Call(() => ((IDeactivate)vm).DeactivateAsync(true)));
    }

    private static async Task RoomLifecycleChecks(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var host = await ui.Call(() => new Conductor<Screen>());
        var vm = await ui.Call(() => new ZimmerViewModel(() => coordinator));
        await (await ui.Call(() => ((IActivate)host).ActivateAsync()));
        await (await ui.Call(() => host.ActivateItemAsync(vm)));
        var read = await source.Next();
        await (await ui.Call(() => ((IDeactivate)host).DeactivateAsync(false)));
        read.Result.SetResult(RoomData("Belegt"));
        RoomCheck(read.Token.IsCancellationRequested && vm.Rooms.Count == 0);
        await (await ui.Call(() => ((IActivate)host).ActivateAsync()));
        (await source.Next()).Result.SetResult(RoomData()); var tick = await clock.Live();
        RoomCheck(vm.Rooms.Count == 1 && vm.SelectedRoom == null);
        var calls = source.Calls;
        await (await ui.Call(() => ((IActivate)vm).ActivateAsync())); RoomCheck(source.Calls == calls);
        await ui.Call(() => { vm.SelectedRoom = vm.Rooms[0]; vm.Beschreibung = "logout draft"; return true; });
        tick.Done.SetResult(); read = await source.Next();
        await (await ui.Call(() => ((IDeactivate)host).DeactivateAsync(true)));
        read.Result.SetResult(RoomData(include: false));
        await coordinator.DisposeAsync();
        RoomCheck(vm.Rooms.Count == 1 && vm.Beschreibung == "logout draft" && source.Maximum == 1);
    }

    private static async Task RoomRepositoryConflictChecks(Ui ui)
    {
        foreach (var delete in new[] { false, true })
        {
            var source = new Source(); var clock = new Clock();
            await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
            var writes = 0; var dialogs = 0; RoomOriginalValues? sent = null; var lastMessage = "";
            var result = RoomWriteResult.ConcurrencyConflict;
            var baseline = RoomData().Rooms[0].OriginalValues! with { RoomNumber = "0101", DateAdded = null };
            var model = new Room { Id = baseline.Id, RoomNumber = 101, Type = baseline.Type,
                Capacity = baseline.Capacity, PricePerNight = baseline.PricePerNight, Status = baseline.Status,
                OriginalValues = baseline };
            var data = PollingSnapshot.FromModels(new[] { model }, Array.Empty<ReservationOverview>());
            RoomCheck(data.Rooms.Single().OriginalValues == baseline && data.Rooms.Single().Added == default);
            var vm = await ui.Call(() => new ZimmerViewModel(() => coordinator,
                update: (_, expected) => { sent = expected; writes++; return result; },
                delete: expected => { sent = expected; writes++; return result; },
                showDialog: (text, _, buttons, _) => { lastMessage = text; dialogs++; return buttons == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK; }));
            await (await ui.Call(() => ((IActivate)vm).ActivateAsync()));
            (await source.Next()).Result.SetResult(data); await clock.Live();
            await ui.Call(() => { vm.SelectedRoom = vm.Rooms.Single(); vm.PriceText = "88,50";
                vm.Beschreibung = "Keep my draft"; if (delete) vm.Löschen(); else vm.Aktualisieren(); return true; });
            RoomCheck(sent == baseline && sent.RoomNumber == "0101" && sent.DateAdded == null && writes == 1);
            RoomCheck(vm.SelectedRoom?.Id == 1 && vm.PriceText == "88,50" && vm.Beschreibung == "Keep my draft");
            RoomCheck(vm.RoomConflictWarning.Length > 0 && !vm.CanAktualisieren && !vm.CanLöschen);
            RoomCheck(dialogs == (delete ? 1 : 0)); // Repository conflicts are non-modal.
            (await source.Next()).Result.SetResult(data); await clock.Live();
            RoomCheck(!vm.CanAktualisieren); // Even a returned-to-baseline snapshot must not unlock a conflict.
            await ui.Call(() => { if (delete) vm.Löschen(); else vm.Aktualisieren(); return true; });
            RoomCheck(writes == 1); // No automatic or manual stale retry.
            (await source.Next()).Result.SetResult(data); var tick = await clock.Live();
            var newer = baseline with { RoomNumber = "101", Description = "Other administrator" };
            var fresh = new PollingSnapshot(new[] { data.Rooms[0] with { Description = newer.Description, OriginalValues = newer } }, data.Bookings);
            tick.Done.SetResult(); (await source.Next()).Result.SetResult(fresh); await clock.Live();
            RoomCheck(vm.PriceText == "88,50" && vm.Beschreibung == "Keep my draft" && !vm.CanAktualisieren);
            await ui.Call(() => { vm.ReloadSelectedRoom(); return true; });
            RoomCheck(vm.CanAktualisieren && vm.RoomConflictWarning == "" && vm.Beschreibung == newer.Description);
            (await source.Next()).Result.SetResult(fresh); await clock.Live();
            result = RoomWriteResult.CommittedWithCleanupFailure;
            await ui.Call(() => { if (delete) vm.Löschen(); else vm.Aktualisieren(); return true; });
            RoomCheck(writes == 2 && sent == newer);
            RoomCheck(lastMessage.Contains("Die Änderung wurde gespeichert") && lastMessage.Contains("nicht erneut"));
            (await source.Next()).Result.SetResult(fresh); await clock.Live();
            await (await ui.Call(() => ((IDeactivate)vm).DeactivateAsync(true)));
        }
    }
}
