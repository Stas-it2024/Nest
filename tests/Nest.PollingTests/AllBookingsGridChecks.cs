using Caliburn.Micro;
using Nest.Models;
using Nest.ViewModels.Admin;
using Nest.Synchronization;
using Nest.ViewModels.Reception;
using Nest.Views.Behaviors;
using Nest.Views.Reception;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.CompilerServices;

internal static partial class Program
{
    private static int allGridAssertions;
    private static void AllGridCheck(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("AllBookings grid: " + message);
        allGridAssertions++;
    }
    private static PollingSnapshot AllGridData(string? status = null) => new(Data().Rooms,
        Enumerable.Range(0, 1000).Select(i => Data().Bookings[0] with
        {
            Code = i == 0 ? "BNR-DDDDDDDDDDDDDDDD" : "BNR-" + i.ToString("D16"),
            Guest = "Alexandra Muster", Status = status ?? new[] { "Reserviert", "Eingecheckt", "Ausgecheckt", "Storniert" }[i % 4],
            Total = 99999.99m, PaymentMethod = "Ueberweisung"
        }));
    private static ObservableCollection<AllBookingsViewModel.TicketOrderItem> GridTickets() => new(
        Enumerable.Range(0, 1000).Select(i => new AllBookingsViewModel.TicketOrderItem
        {
            TicketCode = "TKT-20261112-DDDDDDDDDDDD", GuestName = "Alexandra Muster",
            GuestEmail = "guest@example.test", GuestPhone = "+49 151 12345678", Quantity = 999,
            UnitPrice = 99999.99m, TotalPrice = 99999.99m, DateOrdered = new DateTime(2026, 11, 12)
        }));
    private static DataGrid PilotGrid(AllBookingsView view, int tab) =>
        (DataGrid)view.FindName(tab == 0 ? "BookingGrid" : "TicketGrid");
    private static void SelectGridTab(AllBookingsView view, int tab) =>
        ((TabControl)view.FindName("BookingTabs")).SelectedIndex = tab;

    private static void MeasurePilotColumns(DataGrid grid)
    {
        foreach (var column in grid.Columns)
        {
            var header = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == column);
            var cell = Visuals<DataGridCell>(grid).First(c => c.Column == column);
            var text = Visuals<TextBlock>(cell).First();
            double Width(string value, Typeface face, double size) => new FormattedText(value,
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size,
                Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip).WidthIncludingTrailingWhitespace;
            var titleWidth = Width(column.Header.ToString()!,
                new Typeface(header.FontFamily, header.FontStyle, header.FontWeight, header.FontStretch), header.FontSize);
            var textFace = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
            var widestHex = "0123456789ABCDEF".OrderByDescending(c => Width(c.ToString(), textFace, text.FontSize)).First();
            var samples = column.SortMemberPath switch
            {
                "ReservationCode" => new[] { text.Text, "BNR-" + new string(widestHex, 16) },
                "TicketCode" => new[] { text.Text, "TKT-20261112-" + new string(widestHex, 12) },
                "Status" => new[] { "Reserviert", "Eingecheckt", "Ausgecheckt", "Storniert" },
                _ => new[] { text.Text }
            };
            var valueWidth = samples.Max(value => Width(value, textFace, text.FontSize));
            var requiredHeader = Math.Ceiling(titleWidth + header.Padding.Left + header.Padding.Right + 22);
            var requiredValue = Math.Ceiling(valueWidth + cell.Padding.Left + cell.Padding.Right +
                text.Margin.Left + text.Margin.Right + 4); // Includes keyboard-focus border/rounding.
            AllGridCheck(column.MinWidth >= requiredHeader, "header/sort minimum: " + column.Header);
            AllGridCheck(column.MinWidth >= requiredValue, $"sample minimum: {column.Header}; needs {requiredValue}, has {column.MinWidth}");
            AllGridCheck(cell.Padding == new Thickness(6, 0, 6, 0), "symmetric padding");
            AllGridCheck(column.CanUserResize && column.CanUserSort, "resize and sorting allowed");
            foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
            {
                column.SortDirection = direction;
                header.UpdateLayout();
                var label = Visuals<TextBlock>(header).First(t => t.Text == column.Header.ToString());
                AllGridCheck(label.ActualWidth + 1 >= titleWidth, "sorted header title viewport");
            }
            column.SortDirection = null;
            Console.WriteLine($"PILOT {column.Header}: header={requiredHeader}, sample={requiredValue}, min={column.MinWidth}, width={column.Width}");
        }
    }
    private static void ResizeThroughNativeGripper(DataGrid grid, int index, double delta, bool cancel = false)
    {
        var header = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns[index]);
        var thumb = (Thumb)header.Template.FindName("PART_RightHeaderGripper", header);
        AllGridCheck(thumb != null && thumb.IsEnabled, "native resize gripper retained");
        var before = grid.Columns[index].ActualWidth;
        thumb!.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        thumb.RaiseEvent(new DragDeltaEventArgs(delta, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        thumb.RaiseEvent(new DragCompletedEventArgs(delta, 0, cancel) { RoutedEvent = Thumb.DragCompletedEvent });
        grid.UpdateLayout();
        AllGridCheck(Math.Abs(grid.Columns[index].ActualWidth - before - (cancel ? 0 : delta)) < 2,
            $"native drag changes requested column width; before={before}, after={grid.Columns[index].ActualWidth}, delta={delta}");
    }

    private static async Task AllBookingsGridChecks(Ui ui)
    {
        var source = new Source();
        var clock = new Clock();
        await using var coordinator = new PollingCoordinator(source, ui, clock.Delay);
        var vm = await ui.Call(() => new AllBookingsViewModel(false, () => coordinator));
        await (await ui.Call(() => ((IActivate)vm).ActivateAsync()));
        (await source.Next()).Result.SetResult(AllGridData());
        await clock.Live();
        var session = new DataGridColumnWidthSession();
        var fixture = await ui.Call(() =>
        {
            vm.TicketOrders = GridTickets();
            var view = new AllBookingsView(session) { DataContext = vm };
            var host = new Border { Child = view, Width = 2000, Height = 900 };
            var hwnd = new HwndSource(new HwndSourceParameters("Offline AllBookings tables")
            { Width = 2000, Height = 900, WindowStyle = unchecked((int)0x80000000) });
            hwnd.RootVisual = host;
            MeasureLayout(host, 2000, 900);
            return (view, host, hwnd);
        });
        var (view, host, hwnd) = fixture;
        try
        {
            await EmptyAllBookingsGrids(ui, host, coordinator, vm);
            await ui.Call(() => { host.Child = view; return true; });
            foreach (var tab in new[] { 0, 1 })
            {
                await ui.Call(() => { SelectGridTab(view, tab); return true; });
                await SetLayout(ui, host, 2000, 900);
                await ui.Call(() =>
                {
                    var grid = PilotGrid(view, tab);
                    AllGridCheck(grid.Columns.Count == (tab == 0 ? 12 : 8), "original column count");
                    MeasurePilotColumns(grid);
                    AllGridCheck(grid.RowHeaderWidth == 40 && grid.HeadersVisibility == DataGridHeadersVisibility.All, "row headers preserved");
                    AllGridCheck(grid.IsReadOnly && !grid.CanUserAddRows && !grid.CanUserDeleteRows, "read-only policy unchanged");
                    AllGridCheck(grid.EnableRowVirtualization && grid.EnableColumnVirtualization &&
                        VirtualizingPanel.GetVirtualizationMode(grid) == VirtualizationMode.Recycling, "virtualization profile");
                    AllGridCheck(Visuals<DataGridRow>(grid).Count() is > 0 and < 100, "realized rows bounded");
                    AllGridCheck(grid.CellStyle.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsKeyboardFocusWithinProperty), "keyboard focus cue");
                    return true;
                });
                foreach (var (width, height) in new[] { (1440d,850d), (1200d,800d), (800d,600d), (600d,500d), (320d,500d), (2000d,900d) })
                {
                    await SetLayout(ui, host, width, height);
                    await ui.Call(() =>
                    {
                        var grid = PilotGrid(view, tab);
                        var scroll = Visuals<ScrollViewer>(grid).First();
                        AllGridCheck(grid.ActualWidth <= width && grid.ActualHeight > 0 && grid.ActualHeight < height, "bounded viewport");
                        foreach (var name in new[] { "FilterReservations", "ClearSearch", "Refresh" })
                        {
                            var button = (Button)view.FindName(name);
                            var position = button.TranslatePoint(new Point(), view);
                            AllGridCheck(position.X >= 0 && position.X + button.ActualWidth <= view.ActualWidth + 1 &&
                                position.Y >= 0 && position.Y + button.ActualHeight <= view.ActualHeight,
                                "wrapped toolbar button remains reachable: " + name);
                            AllGridCheck(button.ActualWidth >= (name == "FilterReservations" ? 80 : 100), "readable button size");
                        }
                        var toolbar = (WrapPanel)view.FindName("SearchToolbar");
                        var bounds = toolbar.Children.Cast<FrameworkElement>()
                            .Select(child => new Rect(child.TranslatePoint(new Point(), view), child.RenderSize)).ToArray();
                        for (var i = 0; i < bounds.Length; i++)
                        {
                            AllGridCheck(bounds[i].X >= 0 && bounds[i].Right <= view.ActualWidth + 1,
                                "complete toolbar group remains within viewport");
                            for (var j = i + 1; j < bounds.Length; j++)
                            {
                                var overlap = Rect.Intersect(bounds[i], bounds[j]);
                                AllGridCheck(overlap.IsEmpty || overlap.Width < 0.5 || overlap.Height < 0.5,
                                    "toolbar groups do not overlap after wrapping");
                            }
                        }
                        AllGridCheck(grid.Columns.All(c => c.ActualWidth >= c.MinWidth - 1), "content minima respected");
                        AllGridCheck(grid.Columns.Count(c => c.Width.IsStar) == 2, "layout did not save automatic star recalculation as manual width");
                        var available = grid.ActualWidth - 2 - grid.RowHeaderWidth -
                            (scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0);
                        if (available >= grid.Columns.Sum(c => c.MinWidth))
                            AllGridCheck(Math.Abs(grid.Columns.Sum(c => c.ActualWidth) - available) <= 2, "no unused filler before manual resizing");
                        else AllGridCheck(scroll.ScrollableWidth > 0, "horizontal scrolling instead of squeezing");
                        return true;
                    });
                }
                await ui.Call(() =>
                {
                    var grid = PilotGrid(view, tab);
                    ResizeThroughNativeGripper(grid, 1, 64); // Text/star column.
                    ResizeThroughNativeGripper(grid, tab == 0 ? 2 : 0, 48); // Fixed-width column.
                    return true;
                });
                await SetLayout(ui, host, 2000, 900); // Finish native resize before recording session widths.
            }
            var manual = await ui.Call(() => new[] { PilotGrid(view, 0).Columns[1].ActualWidth,
                PilotGrid(view, 0).Columns[2].ActualWidth, PilotGrid(view, 1).Columns[1].ActualWidth,
                PilotGrid(view, 1).Columns[0].ActualWidth });
            coordinator.RequestRefresh();
            (await source.Next()).Result.SetResult(AllGridData("Eingecheckt"));
            await clock.Live();
            await ui.Call(() => { vm.TicketOrders = GridTickets(); SelectGridTab(view, 0); return true; });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                var grid = PilotGrid(view, 0);
                AllGridCheck(vm.Reservations[0].Status == "Eingecheckt", "real coordinator applies changed snapshot");
                AllGridCheck(Math.Abs(grid.Columns[1].ActualWidth - manual[0]) < 1 && Math.Abs(grid.Columns[2].ActualWidth - manual[1]) < 1,
                    "manual widths survive polling/tab/resize");
                grid.SelectedItem = vm.Reservations[5];
                CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Add(new SortDescription("ReservationCode", ListSortDirection.Descending));
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                var grid = PilotGrid(view, 0);
                AllGridCheck(grid.SelectedItem == vm.SelectedReservation && vm.SelectedReservation != null, "selection binding preserved");
                AllGridCheck(CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Count == 1, "sort preserved");
                var scroll = Visuals<ScrollViewer>(grid).First();
                scroll.ScrollToRightEnd();
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                var scroll = Visuals<ScrollViewer>(PilotGrid(view, 0)).First();
                AllGridCheck(scroll.HorizontalOffset > 0, "rightmost columns reachable");
                host.Child = new TextBlock { Text = "Another page" };
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                AllGridCheck(!view.IsLoaded, "old page unloaded");
                PilotGrid(view, 0).Columns[1].Width = 777; // Detached descriptors must not pollute session state.
                var nextVm = new AllBookingsViewModel(true, () => coordinator)
                { Reservations = new ObservableCollection<ReservationOverview>(vm.Reservations), TicketOrders = GridTickets() };
                view = new AllBookingsView(session) { DataContext = nextVm };
                host.Child = view;
                return true;
            });
            foreach (var tab in new[] { 0, 1 })
            {
                await ui.Call(() => { SelectGridTab(view, tab); return true; });
                await SetLayout(ui, host, 1200, 800);
                await ui.Call(() =>
                {
                    var grid = PilotGrid(view, tab);
                    AllGridCheck(Math.Abs(grid.Columns[1].ActualWidth - manual[tab * 2]) < 1, "new View/VM restores text width");
                    AllGridCheck(Math.Abs(grid.Columns[tab == 0 ? 2 : 0].ActualWidth - manual[tab * 2 + 1]) < 1, "new View restores fixed width");
                    return true;
                });
            }
            await ui.Call(() => { host.Child = null; return true; });
            await SetLayout(ui, host, 1200, 800);
            var restartedSession = new DataGridColumnWidthSession();
            await ui.Call(() =>
            {
                view = new AllBookingsView(restartedSession) { DataContext = vm };
                host.Child = view;
                return true;
            });
            foreach (var tab in new[] { 0, 1 })
            {
                await ui.Call(() => { SelectGridTab(view, tab); return true; });
                await SetLayout(ui, host, 1200, 800);
                await ui.Call(() =>
                {
                    var grid = PilotGrid(view, tab);
                    AllGridCheck(grid.Columns[1].Width.IsStar && grid.Columns[tab == 0 ? 2 : 0].Width.Value == (tab == 0 ? 88 : 256),
                        "fresh process-session starts with XAML defaults");
                    ResizeThroughNativeGripper(grid, 1, 32, cancel: true);
                    AllGridCheck(grid.Columns.Count(c => c.Width.IsStar) == 2, "cancelled drag restores adaptive widths");
                    for (var index = 0; index < grid.Columns.Count; index++)
                    {
                        ResizeThroughNativeGripper(grid, index, 44 - grid.Columns[index].ActualWidth);
                        AllGridCheck(grid.Columns[index].MinWidth == 44 && Math.Abs(grid.Columns[index].ActualWidth - 44) < 1,
                            "manual shrinking below content allowed for column " + index);
                        var header = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns[index]);
                        var cell = Visuals<DataGridCell>(grid).First(c => c.Column == grid.Columns[index]);
                        var text = Visuals<TextBlock>(cell).First();
                        AllGridCheck(Equals(header.ToolTip, header.Content) && Equals(text.ToolTip, text.Text),
                            "full header and formatted value tooltip after shrinking");
                    }
                    return true;
                });
            }
            await ui.Call(() => { SelectGridTab(view, 0); return true; });
            await SetLayout(ui, host, 1200, 800);
            await ui.Call(() =>
            {
                var grid = PilotGrid(view, 0);
                ResizeThroughNativeGripper(grid, 2, 32);
                var header = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns[2]);
                var thumb = (Thumb)header.Template.FindName("PART_RightHeaderGripper", header);
                // WPF recreates this direct event on each ancestor Control with the same OriginalSource.
                thumb.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Control.MouseDoubleClickEvent, Source = thumb });
                grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Control.MouseDoubleClickEvent, Source = thumb });
                AllGridCheck(grid.Columns[2].Width.IsAuto, "native double-click enables Auto");
                host.Child = new TextBlock();
                return true;
            });
            await SetLayout(ui, host, 1200, 800);
            await ui.Call(() =>
            {
                view = new AllBookingsView(restartedSession) { DataContext = vm };
                host.Child = view;
                return true;
            });
            await SetLayout(ui, host, 1200, 800);
            await ui.Call(() =>
            {
                AllGridCheck(PilotGrid(view, 0).Columns[2].Width.IsAuto, "explicit Auto choice survives navigation");
                vm.Parent = (ReceptionViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ReceptionViewModel));
                host.Child = new AllBookingsView { DataContext = vm }; // Production constructor/session wiring.
                return true;
            });
            await SetLayout(ui, host, 1200, 800);
            await ui.Call(() =>
            {
                ResizeThroughNativeGripper(PilotGrid((AllBookingsView)host.Child, 0), 2, 24);
                return true;
            });
            await SetLayout(ui, host, 1200, 800);
            await ui.Call(() => { host.Child = new AllBookingsView { DataContext = vm }; return true; });
            await SetLayout(ui, host, 1200, 800);
            await ui.Call(() =>
            {
                AllGridCheck(Math.Abs(PilotGrid((AllBookingsView)host.Child, 0).Columns[2].ActualWidth - 112) < 1,
                    "production constructors share the authenticated navigation owner's session");
                return true;
            });
            foreach (var ownerType in new[] { typeof(ReceptionViewModel), typeof(AdminViewModel) })
            {
                await ui.Call(() => { SelectGridTab((AllBookingsView)host.Child, 1); return true; });
                await SetLayout(ui, host, 1200, 800);
                await ui.Call(() =>
                {
                    ResizeThroughNativeGripper(PilotGrid((AllBookingsView)host.Child, 1), 0, -100);
                    vm.Parent = null;
                    host.Child = new TextBlock { Text = "Login after Abmelden" };
                    return true;
                });
                await SetLayout(ui, host, 1200, 800);
                await ui.Call(() =>
                {
                    var nextLogin = new AllBookingsViewModel(false, () => coordinator)
                    {
                        Parent = RuntimeHelpers.GetUninitializedObject(ownerType),
                        Reservations = new ObservableCollection<ReservationOverview>(vm.Reservations), TicketOrders = GridTickets()
                    };
                    view = new AllBookingsView { DataContext = nextLogin };
                    host.Child = view;
                    return true;
                });
                foreach (var tab in new[] { 0, 1 })
                {
                    await ui.Call(() => { SelectGridTab(view, tab); return true; });
                    await SetLayout(ui, host, 1200, 800);
                    await ui.Call(() =>
                    {
                        var grid = PilotGrid(view, tab);
                        AllGridCheck(grid.Columns[1].Width.IsStar && grid.Columns[tab == 0 ? 2 : 0].Width.Value == (tab == 0 ? 88 : 256),
                            "logout/relogin owner does not inherit earlier widths");
                        return true;
                    });
                }
            }
        }
        finally
        {
            await ui.Call(() => { host.Child = null; hwnd.RootVisual = null; hwnd.Dispose(); return true; });
            await (await ui.Call(() => ((IDeactivate)vm).DeactivateAsync(true)));
        }
        Console.WriteLine($"PASS: {allGridAssertions} AllBookings DataGrid assertions; compiled View, native gripper events, real polling with fake data, no SQL.");
    }

    private static async Task EmptyAllBookingsGrids(Ui ui, Border host, PollingCoordinator coordinator, AllBookingsViewModel populated)
    {
        var empty = await ui.Call(() =>
        {
            var model = new AllBookingsViewModel(false, () => coordinator);
            var result = new AllBookingsView(new DataGridColumnWidthSession()) { DataContext = model };
            host.Child = result;
            return result;
        });
        var accessible = true;
        var manualWidths = new double[2];
        foreach (var tab in new[] { 0, 1 })
        {
            await ui.Call(() => { SelectGridTab(empty, tab); return true; });
            foreach (var width in new[] { 2000d, 600d, 800d, 2000d })
            {
                await SetLayout(ui, host, width, 600);
                await ui.Call(() =>
                {
                    var grid = PilotGrid(empty, tab);
                    var scroll = Visuals<ScrollViewer>(grid).First();
                    var total = grid.Columns.Sum(c => c.ActualWidth);
                    var available = grid.ActualWidth - 2; // No realized row-header gutter in an empty DataGrid.
                    Console.WriteLine($"EMPTY tab={tab}, width={width}, grid={grid.ActualWidth:F0}, columns={total:F0}, viewport={scroll.ViewportWidth:F0}, extent={scroll.ExtentWidth:F0}, scrollable={scroll.ScrollableWidth:F0}, bar={scroll.ComputedHorizontalScrollBarVisibility}, logical={ScrollViewer.GetCanContentScroll(grid)}");
                    AllGridCheck(!grid.HasItems && grid.ActualWidth <= width && grid.ActualHeight > 0,
                        "empty grid uses bounded viewport, without dummy rows");
                    accessible &= total <= available + 1
                        ? scroll.ScrollableWidth < 1 && scroll.ComputedHorizontalScrollBarVisibility != Visibility.Visible
                        : scroll.ScrollableWidth > 0 && scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible;
                    return true;
                });
                if (width < 1000)
                {
                    await ui.Call(() => { Visuals<ScrollViewer>(PilotGrid(empty, tab)).First().ScrollToRightEnd(); return true; });
                    await SetLayout(ui, host, width, 600);
                    await ui.Call(() =>
                    {
                        var grid = PilotGrid(empty, tab);
                        var header = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns.Last());
                        var point = header.TranslatePoint(new Point(), grid);
                        AllGridCheck(Visuals<ScrollViewer>(grid).First().HorizontalOffset > 0 &&
                            point.X >= 0 && point.X + header.ActualWidth <= grid.ActualWidth + 1,
                            "last empty-table header is actually reachable by horizontal scrolling");
                        return true;
                    });
                }
            }
            await ui.Call(() =>
            {
                var grid = PilotGrid(empty, tab);
                ResizeThroughNativeGripper(grid, grid.Columns.Count - 1, 64);
                manualWidths[tab] = grid.Columns.Last().ActualWidth;
                return true;
            });
        }
        AllGridCheck(accessible, "empty grids expose all overflowing headers with no unnecessary wide scrollbar");
        await ui.Call(() =>
        {
            var model = (AllBookingsViewModel)empty.DataContext;
            model.Reservations = new ObservableCollection<ReservationOverview>(populated.Reservations);
            model.TicketOrders = GridTickets();
            return true;
        });
        foreach (var tab in new[] { 0, 1 })
        {
            await ui.Call(() => { SelectGridTab(empty, tab); return true; });
            await SetLayout(ui, host, 600, 600);
            await ui.Call(() =>
            {
                var grid = PilotGrid(empty, tab);
                AllGridCheck(grid.HasItems && ScrollViewer.GetCanContentScroll(grid) &&
                    grid.EnableRowVirtualization && grid.EnableColumnVirtualization &&
                    Visuals<DataGridRow>(grid).Count() is > 0 and < 100, "populated grid returns to native virtualization");
                AllGridCheck(Visuals<DataGridRowsPresenter>(grid).First().MinWidth == 0,
                    "empty-host extent constraint removed when rows arrive");
                AllGridCheck(Math.Abs(grid.Columns.Last().ActualWidth - manualWidths[tab]) < 1,
                    "resize choice from empty table survives arriving data");
                return true;
            });
        }
        await ui.Call(() =>
        {
            var model = (AllBookingsViewModel)empty.DataContext;
            model.Reservations.Clear(); model.TicketOrders.Clear();
            return true;
        });
        foreach (var tab in new[] { 0, 1 })
        {
            await ui.Call(() => { SelectGridTab(empty, tab); return true; });
            await SetLayout(ui, host, 600, 600);
            await ui.Call(() =>
            {
                var grid = PilotGrid(empty, tab);
                AllGridCheck(grid.Items.Count == 0 && Visuals<DataGridRow>(grid).Count() == 0 &&
                    Visuals<ScrollViewer>(grid).First().ScrollableWidth > 0,
                    "clearing populated table restores empty header scrolling without dummy rows");
                return true;
            });
        }
    }
}
