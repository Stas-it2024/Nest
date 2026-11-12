using Caliburn.Micro;
using Nest.Synchronization;
using Nest.ViewModels.Reception;
using Nest.ViewModels.Shell;
using Nest.Views.Reception;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static partial class Program
{
    private static int layoutAssertions;
    private static void LayoutCheck(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Reservation layout: " + message);
        layoutAssertions++;
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static T Named<T>(ReservationManagementView view, string name) where T : class => (T)view.FindName(name);
    private static void MeasureLayout(Border host, double width, double height)
    {
        host.Width = width; host.Height = height;
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
    }
    private static async Task SetLayout(Ui ui, Border host, double width, double height)
    {
        await ui.Call(() => { MeasureLayout(host, width, height); return true; });
        // Drain deferred presentation/scroll restoration without a sleep or a visible window.
        await ui.Call(() => host.Dispatcher.InvokeAsync(() => host.UpdateLayout(), DispatcherPriority.ApplicationIdle).Task).Unwrap();
    }
    private static void SelectPane(ReservationManagementView view, bool form)
    {
        var button = Named<RadioButton>(view, form ? "FormPaneChoice" : "ListPaneChoice");
        var peer = new RadioButtonAutomationPeer(button);
        LayoutCheck(!string.IsNullOrWhiteSpace(peer.GetName()), "accessible pane name");
        ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
    }
    private static PollingSnapshot LayoutData(string status = "Reserviert") => new(Data().Rooms,
        Enumerable.Range(0, 1000).Select(i => Data().Bookings[0] with
        { Code = "BNR-" + i.ToString("D16"), Guest = "Guest " + i, Status = status }));

    private static async Task ReservationLayoutChecks(Ui ui)
    {
        var source = new Source();
        var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var vm = await ui.Call(() => new ReservationManagementViewModel(
            (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel)), () => coordinator));
        await (await ui.Call(() => ((IActivate)vm).ActivateAsync()));
        (await source.Next()).Result.SetResult(LayoutData());
        await clock.Live();
        var fixture = await ui.Call(() =>
        {
            foreach (var resource in new[] { "Palette", "CommonStyles", "ReceptionFormStyles", "DataGridStyles", "ButtonStyles" })
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Nest;component/Styles/" + resource + ".xaml") });
            vm.FirstName = "Draft name"; vm.LastName = "Retained"; vm.SpecialRequests = "Unfinished request";
            vm.SelectedRoom = vm.RoomList.Single();
            vm.SelectedReservation = vm.Reservations[4];
            vm.SearchText = "Guest";
            var view = new ReservationManagementView { DataContext = vm };
            var host = new Border { Child = view, Width = 1440, Height = 850 };
            // Real presentation source, deliberately no WS_VISIBLE/activation and no Nest startup.
            var hwnd = new HwndSource(new HwndSourceParameters("Offline reservation layout")
            { Width = 1440, Height = 850, WindowStyle = unchecked((int)0x80000000) });
            hwnd.RootVisual = host;
            MeasureLayout(host, 1440, 850);
            return (view, host, hwnd);
        });
        var (view, host, hwnd) = fixture;
        try
        {
            await SetLayout(ui, host, 1440, 850);
            var state = await ui.Call(() =>
            {
                LayoutCheck(view.IsLoaded, "real loaded View");
                var grid = Named<DataGrid>(view, "ReservationGrid");
                var form = Named<ScrollViewer>(view, "FormScroller");
                var price = Visuals<TextBox>(form).Single(t => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path == "Price");
                price.Text = "not-a-price";
                var collectionView = CollectionViewSource.GetDefaultView(vm.Reservations);
                collectionView.SortDescriptions.Add(new SortDescription("ReservationCode", ListSortDirection.Descending));
                LayoutCheck(Validation.GetHasError(price), "invalid decimal stays in target");
                grid.UpdateLayout();
                // Sorting may discard cell containers before the next render pass.
                // Any grid descendant must be normalized to the stable grid target.
                UIElement descendant = Visuals<DataGridCell>(grid).FirstOrDefault() is { } cell
                    ? cell : Visuals<ScrollViewer>(grid).First();
                view.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, descendant)
                    { RoutedEvent = Keyboard.PreviewGotKeyboardFocusEvent });
                var remembered = typeof(ReservationManagementView).GetField("lastListFocus",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view);
                LayoutCheck(ReferenceEquals(remembered, grid), "remember stable grid, never a recycled row/cell");
                return (grid, form, price, selected: vm.SelectedReservation!.ReservationCode,
                    room: vm.SelectedRoom, amount: vm.Price, collection: vm.Reservations, collectionView);
            });
            for (var cycle = 0; cycle < 3; cycle++)
            foreach (var (width, height, wide, narrow) in new[]
            { (1440d,850d,true,false), (1200d,800d,true,false), (800d,600d,false,false),
              (600d,500d,false,true), (1200d,320d,false,false), (1440d,850d,true,false) })
            {
                await SetLayout(ui, host, width, height);
                await ui.Call(() =>
                {
                    LayoutCheck(view.ActualWidth == width && view.ActualHeight == height, "actual allocated size");
                    LayoutCheck(view.IsWideLayout == wide && view.IsSingleColumnForm == narrow, "expected mode");
                    LayoutCheck(double.IsNaN(view.Width), "no fixed View width");
                    return true;
                });
                foreach (var showForm in new[] { false, true })
                {
                    if (!wide) await ui.Call(() => { SelectPane(view, showForm); return true; });
                    await SetLayout(ui, host, width, height);
                    await ui.Call(() =>
                    {
                        var formPanel = Named<Border>(view, "GuestFormPanel");
                        var listPanel = Named<Border>(view, "ListPanel");
                        LayoutCheck(formPanel.Visibility == (wide || showForm ? Visibility.Visible : Visibility.Collapsed), "form visibility");
                        LayoutCheck(listPanel.Visibility == (wide || !showForm ? Visibility.Visible : Visibility.Collapsed), "list visibility");
                        LayoutCheck(ReferenceEquals(state.grid, Named<DataGrid>(view, "ReservationGrid")) &&
                            ReferenceEquals(state.form, Named<ScrollViewer>(view, "FormScroller")), "control identity");
                        if (wide || showForm)
                        {
                            var save = Named<Button>(view, "SaveReservation");
                            var position = save.TranslatePoint(new Point(), view);
                            LayoutCheck(position.Y >= 0 && position.Y + save.ActualHeight <= height + 1, "save within vertical bounds");
                            LayoutCheck(position.X >= 0 && position.X + save.ActualWidth <= width + 1, "save within horizontal bounds");
                            LayoutCheck(state.form.ViewportHeight > 0 && state.form.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "form viewport");
                            var pair = Named<Grid>(view, "NamePair");
                            LayoutCheck(Grid.GetRow((UIElement)pair.Children[1]) == (narrow ? 1 : 0), "single column pairs");
                        }
                        if (wide || !showForm)
                        {
                            var realized = Visuals<DataGridRow>(state.grid).Count();
                            LayoutCheck(realized > 0 && realized < 100, "row virtualization");
                            LayoutCheck(state.grid.ActualHeight > 0 && state.grid.ActualHeight < height, "bounded table");
                            foreach (var name in new[] { "ReservationSearch", "ClearSearch", "CheckIn", "CheckOut", "CancelReservation" })
                            {
                                var control = Named<FrameworkElement>(view, name);
                                var position = control.TranslatePoint(new Point(), view);
                                LayoutCheck(position.X >= 0 && position.X + control.ActualWidth <= width + 1, "toolbar fits: " + name);
                            }
                        }
                        LayoutCheck(state.price.Text == "not-a-price" && Validation.GetHasError(state.price) && vm.Price == state.amount, "unconverted text retained");
                        LayoutCheck(vm.FirstName == "Draft name" && vm.LastName == "Retained" && vm.SpecialRequests == "Unfinished request", "draft fields retained");
                        LayoutCheck(ReferenceEquals(vm.SelectedRoom, state.room) && vm.SearchText == "Guest", "room and search retained");
                        LayoutCheck(vm.SelectedReservation?.ReservationCode == state.selected && ReferenceEquals(state.grid.SelectedItem, vm.SelectedReservation), "selection retained");
                        LayoutCheck(ReferenceEquals(vm.Reservations, state.collection) && state.collectionView.SortDescriptions.Count == 1, "collection and sort retained");
                        LayoutCheck(vm.ActiveItem == null && vm.IsActive, "no business navigation or deactivation");
                        return true;
                    });
                }
            }
            // Check both edges of width, height and narrow-mode hysteresis.
            var statusHeight = await ui.Call(() =>
            {
                var status = Named<StackPanel>(view, "StatusArea");
                return status.ActualHeight + status.Margin.Top + status.Margin.Bottom;
            });
            foreach (var (width, height, wide, narrow) in new[]
            { (1175d,800d,true,false), (1161d,800d,true,false), (1159d,800d,false,false),
              (1185d,800d,false,false), (1200d,800d,true,false), (1200d,statusHeight+479d,true,false),
              (1200d,statusHeight+459d,false,false), (1200d,statusHeight+499d,false,false), (1200d,statusHeight+520d,true,false),
              (639d,600d,false,true), (650d,600d,false,true), (660d,600d,false,false) })
            {
                await SetLayout(ui, host, width, height);
                await ui.Call(() =>
                {
                    LayoutCheck(view.IsWideLayout == wide && view.IsSingleColumnForm == narrow, "hysteresis");
                    return true;
                });
            }
            await ui.Call(() => { SelectPane(view, true); return true; });
            await SetLayout(ui, host, 600, 500);
            await ui.Call(() => { state.form.ScrollToEnd(); return true; });
            await SetLayout(ui, host, 600, 500);
            var offset = await ui.Call(() => state.form.VerticalOffset);
            LayoutCheck(offset > 0, "narrow form scrolls");
            await ui.Call(() => { SelectPane(view, false); return true; });
            await SetLayout(ui, host, 600, 500);
            await ui.Call(() => { SelectPane(view, true); return true; });
            await SetLayout(ui, host, 600, 500);
            await ui.Call(() => { LayoutCheck(Math.Abs(state.form.VerticalOffset - offset) < 1, "form scroll restored"); return true; });

            await ui.Call(() => { SelectPane(view, false); return true; });
            await SetLayout(ui, host, 600, 500);
            var listScroll = await ui.Call(() => Visuals<ScrollViewer>(state.grid).First());
            await ui.Call(() => { listScroll.ScrollToEnd(); listScroll.ScrollToHorizontalOffset(80); return true; });
            await SetLayout(ui, host, 600, 500);
            var listOffsets = await ui.Call(() => (listScroll.VerticalOffset, listScroll.HorizontalOffset));
            LayoutCheck(listOffsets.VerticalOffset > 0 && listOffsets.HorizontalOffset > 0, "both table scroll axes work");
            await ui.Call(() => { SelectPane(view, true); return true; });
            await SetLayout(ui, host, 600, 500);
            await ui.Call(() => { SelectPane(view, false); return true; });
            await SetLayout(ui, host, 600, 500);
            await ui.Call(() =>
            {
                LayoutCheck(Math.Abs(listScroll.VerticalOffset - listOffsets.VerticalOffset) < 1 &&
                    Math.Abs(listScroll.HorizontalOffset - listOffsets.HorizontalOffset) < 1, "table scroll restored");
                LayoutCheck(source.Calls == 1, "mode changes neither resubscribe nor request reads");
                SelectPane(view, true);
                return true;
            });
            await SetLayout(ui, host, 600, 500);

            // The LIST is hidden, but the same active VM must still receive real coordinator callbacks.
            coordinator.RequestRefresh();
            (await source.Next()).Result.SetResult(LayoutData("Eingecheckt"));
            await clock.Live();
            await ui.Call(() =>
            {
                LayoutCheck(vm.SelectedReservation?.Status == "Eingecheckt", "polling while list hidden");
                LayoutCheck(state.price.Text == "not-a-price" && vm.Price == state.amount, "polling preserves invalid draft");
                LayoutCheck(Keyboard.FocusedElement == null, "hidden test host never steals desktop focus");
                LayoutCheck(Named<RadioButton>(view, "FormPaneChoice").Focusable && Named<RadioButton>(view, "ListPaneChoice").IsTabStop, "standard keyboard focusable selector");
                LayoutCheck(Named<Border>(view, "ListPanel").Visibility == Visibility.Collapsed, "inactive area excluded from visual/tab navigation");
                return true;
            });
            await (await ui.Call(() => ((IDeactivate)vm).DeactivateAsync(true)));
            await ui.Call(() => { LayoutCheck(!vm.IsActive, "deactivation unchanged"); return true; });
            await ui.Call(() =>
            {
                SelectPane(view, false); // Queue presentation work, then unload before it can run.
                host.Child = null;
                return true;
            });
            await ui.Call(() => host.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task).Unwrap();
            await ui.Call(() => { LayoutCheck(!view.IsLoaded && Keyboard.FocusedElement == null, "no late focus after unload"); return true; });
        }
        finally
        {
            await ui.Call(() => { hwnd.RootVisual = null; hwnd.Dispose(); return true; });
        }
        Console.WriteLine($"PASS: {layoutAssertions} reservation layout assertions; current compiled View, hidden STA host, fake polling, no SQL. Foreground keyboard/modal/DPI checks require manual testing.");
    }
}
