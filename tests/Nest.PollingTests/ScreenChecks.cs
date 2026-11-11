using Caliburn.Micro;
using Nest.Synchronization;
using Nest.ViewModels.Reception;
using Nest.ViewModels.Shell;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Data;

internal static partial class Program
{
    private static int screenAssertions;
    private static void ScreenCheck(bool value)
    { if (!value) throw new InvalidOperationException("Booking screen polling invariant failed."); screenAssertions++; }
    private static async Task ScreenChecks(Ui ui)
    {
        var pauseToken = typeof(BookingPollingScreen).GetMethod("PausePolling", BindingFlags.Instance | BindingFlags.NonPublic)!.MetadataToken;
        foreach (var command in new[] { "SaveReservation", "CheckIn", "CheckOut", "CancelReservation" })
        {
            var il = typeof(ReservationManagementViewModel).GetMethod(command)!.GetMethodBody()!.GetILAsByteArray()!;
            ScreenCheck(il[0] == 0x02 && il[1] == 0x28 && BitConverter.ToInt32(il, 2) == pauseToken);
        }
        await BookingScreens(ui);
        await ScreenLifecycle(ui);
        Console.WriteLine($"PASS: {screenAssertions} booking-screen assertions; actual ViewModels/Caliburn lifecycle/Dispatcher, no SQL.");
    }
    private static PollingSnapshot ScreenData(string status, bool second = false, string roomStatus = "Verfuegbar")
    {
        var original = Data(status);
        return new PollingSnapshot(new[] { original.Rooms[0] with { Status = roomStatus } },
            second ? original.Bookings.Append(original.Bookings[0] with { Code = "SECOND", Guest = "Other" }) : original.Bookings);
    }
    private static async Task BookingScreens(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        var reception = await ui.Call(() => new ReservationManagementViewModel(shell, () => coordinator));
        var all = await ui.Call(() => new AllBookingsViewModel(true, () => coordinator));
        ScreenCheck(source.Calls == 0 && reception.Reservations.Count == 0 && all.Reservations.Count == 0);
        await (await ui.Call(() => ((IActivate)reception).ActivateAsync()));
        (await source.Next()).Result.SetResult(ScreenData("Reserviert")); await clock.Live();
        await (await ui.Call(() => ((IActivate)all).ActivateAsync()));
        (await source.Next()).Result.SetResult(ScreenData("Reserviert")); var tick = await clock.Live();
        await ui.Call(() =>
        {
            reception.SelectedReservation = reception.Reservations.Single();
            reception.SelectedRoom = reception.RoomList.Single();
            reception.FirstName = "Unfinished"; reception.SpecialRequests = "Keep this"; reception.Deposit = 17;
            all.SelectedReservation = all.Reservations.Single();
            CollectionViewSource.GetDefaultView(reception.Reservations).SortDescriptions.Add(new SortDescription("ReservationCode", ListSortDirection.Descending));
            CollectionViewSource.GetDefaultView(all.Reservations).SortDescriptions.Add(new SortDescription("ReservationCode", ListSortDirection.Descending));
            return true;
        });
        var room = reception.SelectedRoom; var price = reception.Price;
        var controls = await ui.Call(() =>
        {
            var combo = new System.Windows.Controls.ComboBox { ItemsSource = reception.RoomList, DataContext = reception };
            BindingOperations.SetBinding(combo, System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding("SelectedRoom") { Mode = BindingMode.TwoWay });
            var grid = new System.Windows.Controls.DataGrid { ItemsSource = reception.Reservations, DataContext = reception, IsReadOnly = true };
            BindingOperations.SetBinding(grid, System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
                new Binding("SelectedReservation") { Mode = BindingMode.TwoWay });
            return (combo, grid);
        });
        var reservations = reception.Reservations; var overview = all.Reservations;
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(ScreenData("Eingecheckt", true, "Belegt")); tick = await clock.Live();
        ScreenCheck(reception.Reservations.Count == 2 && all.Reservations.Count == 2);
        ScreenCheck(reception.SelectedReservation?.ReservationCode == "CODE" && reception.SelectedReservation.Status == "Eingecheckt");
        ScreenCheck(await ui.Call(() => ReferenceEquals(controls.combo.SelectedItem, room) &&
            ReferenceEquals(controls.grid.SelectedItem, reception.SelectedReservation)));
        ScreenCheck(all.SelectedReservation?.ReservationCode == "CODE" && all.SelectedReservation.Status == "Eingecheckt");
        ScreenCheck(ReferenceEquals(room, reception.SelectedRoom) && reception.Price == price && room!.Status == "Verfuegbar");
        ScreenCheck(reception.FirstName == "Unfinished" && reception.SpecialRequests == "Keep this" && reception.Deposit == 17);
        ScreenCheck(ReferenceEquals(reservations, reception.Reservations) && ReferenceEquals(overview, all.Reservations));
        ScreenCheck(await ui.Call(() => CollectionViewSource.GetDefaultView(reception.Reservations).SortDescriptions.Single().Direction == ListSortDirection.Descending));
        ScreenCheck(await ui.Call(() => CollectionViewSource.GetDefaultView(all.Reservations).SortDescriptions.Single().Direction == ListSortDirection.Descending));
        var before = source.Calls;
        await ui.Call(() => { reception.SearchText = "Guest"; all.SearchText = "Guest"; all.SelectedStatus = "Eingecheckt"; return true; });
        ScreenCheck(source.Calls == before && reception.Reservations.Count == 1 && all.Reservations.Count == 1);
        await ui.Call(() => { all.Refresh(); return true; });
        (await source.Next()).Result.SetResult(ScreenData("Eingecheckt", true, "Belegt")); tick = await clock.Live();
        ScreenCheck(all.Reservations.Count == 1 && all.SelectedStatus == "Eingecheckt" && all.SearchText == "Guest");
        var row = reception.Reservations[0]; var allRow = all.Reservations[0];
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(ScreenData("Eingecheckt", true, "Belegt")); tick = await clock.Live();
        ScreenCheck(ReferenceEquals(row, reception.Reservations[0]) && ReferenceEquals(allRow, all.Reservations[0]));
        foreach (var status in new[] { "Ausgecheckt", "Storniert" })
        {
            await ui.Call(() => { all.SelectedStatus = "Alle"; return true; });
            tick.Done.SetResult(); (await source.Next()).Result.SetResult(ScreenData(status, true)); tick = await clock.Live();
            ScreenCheck(reception.Reservations.Single().Status == status && all.Reservations.Single().Status == status);
            ScreenCheck(reception.SearchText == "Guest" && all.SearchText == "Guest");
        }
        tick.Done.SetResult(); (await source.Next()).Result.SetException(new InvalidOperationException("offline")); tick = await clock.Live();
        ScreenCheck(reception.Reservations.Single().Status == "Storniert" && all.Reservations.Single().Status == "Storniert");
        ScreenCheck(reception.SynchronizationStatus.Contains("Keine Datenbankverbindung") && all.SynchronizationStatus.Contains("Keine Datenbankverbindung"));
        tick.Done.SetResult(); (await source.Next()).Result.SetResult(ScreenData("Reserviert", true)); tick = await clock.Live();
        ScreenCheck(reception.SynchronizationStatus.Contains("Daten aktuell") && all.SynchronizationStatus.Contains("Daten aktuell"));

        // Exercise the very scope used by all four actual command handlers. Keep
        // Dispatcher pumping (as a modal dialog does) while a pre-command read returns.
        await (await ui.Call(() => ((IDeactivate)all).DeactivateAsync(false)));
        var read = await source.Next();
        var pause = await ui.Call(() => (IDisposable)typeof(BookingPollingScreen).GetMethod("PausePolling", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reception, null)!);
        var selected = reception.SelectedReservation;
        read.Result.SetResult(ScreenData("Storniert"));
        await ui.Call(() => true);
        ScreenCheck(ReferenceEquals(selected, reception.SelectedReservation));
        await ui.Call(() => { pause.Dispose(); return true; });
        (await source.Next()).Result.SetResult(ScreenData("Eingecheckt")); tick = await clock.Live();
        ScreenCheck(reception.SelectedReservation?.Status == "Eingecheckt");

        tick.Done.SetResult();
        (await source.Next()).Result.SetResult(new PollingSnapshot(Array.Empty<RoomSnapshot>(), Array.Empty<BookingSnapshot>()));
        await clock.Live();
        ScreenCheck(reception.Reservations.Count == 0 && reception.SelectedReservation == null);
        ScreenCheck(await ui.Call(() => controls.grid.SelectedItem == null && ReferenceEquals(controls.combo.SelectedItem, room)));
        ScreenCheck(ReferenceEquals(room, reception.SelectedRoom) && reception.Price == price && reception.DraftRoomWarning.Length > 0);
        await ui.Call(() => { reception.SelectedRoom = null; return true; });
        ScreenCheck(reception.RoomList.Count == 0 && reception.SelectedRoom == null);
        await ui.Call(() => { BindingOperations.ClearAllBindings(controls.combo); BindingOperations.ClearAllBindings(controls.grid); return true; });
        await (await ui.Call(() => ((IDeactivate)reception).DeactivateAsync(true)));
    }

    private static async Task ScreenLifecycle(Ui ui)
    {
        var source = new Source(); var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var host = await ui.Call(() => new Conductor<Screen>());
        var screen = await ui.Call(() => new AllBookingsViewModel(false, () => coordinator));
        await (await ui.Call(() => ((IActivate)host).ActivateAsync()));
        await (await ui.Call(() => host.ActivateItemAsync(screen)));
        var read = await source.Next();
        await (await ui.Call(() => ((IDeactivate)host).DeactivateAsync(false)));
        ScreenCheck(read.Token.IsCancellationRequested);
        read.Result.SetResult(ScreenData("Storniert"));
        await ui.Call(() => true);
        ScreenCheck(screen.Reservations.Count == 0); // No delivery after parent logout/deactivation.
        await (await ui.Call(() => ((IActivate)host).ActivateAsync()));
        (await source.Next()).Result.SetResult(ScreenData("Reserviert")); var tick = await clock.Live();
        ScreenCheck(screen.Reservations.Single().Status == "Reserviert");
        var calls = source.Calls;
        await (await ui.Call(() => ((IActivate)screen).ActivateAsync()));
        ScreenCheck(source.Calls == calls); // No duplicated subscription on repeated activation.
        await (await ui.Call(() => ((IDeactivate)host).DeactivateAsync(true)));

        var shell = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
        var parent = await ui.Call(() => new ReservationManagementViewModel(shell, () => coordinator));
        await (await ui.Call(() => ((IActivate)parent).ActivateAsync()));
        (await source.Next()).Result.SetResult(ScreenData("Reserviert")); await clock.Live();
        var child = await ui.Call(() => new AllBookingsViewModel(false, () => coordinator));
        var change = typeof(ReservationManagementViewModel).GetMethod("ChangeEmbeddedScreen", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (await ui.Call(() => (Task)change.Invoke(parent, new object?[] { child })!));
        (await source.Next()).Result.SetResult(ScreenData("Eingecheckt")); await clock.Live();
        ScreenCheck(child.IsActive && child.Reservations.Single().Status == "Eingecheckt");
        ScreenCheck(parent.Reservations.Single().Status == "Reserviert");
        await (await ui.Call(() => parent.ShowReservations()));
        (await source.Next()).Result.SetResult(ScreenData("Ausgecheckt")); await clock.Live();
        ScreenCheck(!child.IsActive && parent.Reservations.Single().Status == "Ausgecheckt");
        await (await ui.Call(() => ((IDeactivate)parent).DeactivateAsync(true)));
    }
}
