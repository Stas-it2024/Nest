using Caliburn.Micro;
using Nest.ViewModels.Admin;
using Nest.Views.Admin;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

internal static partial class Program
{
    private static int dashboardLayoutAssertions;
    private static void DashCheck(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Dashboard layout: " + message);
        dashboardLayoutAssertions++;
    }
    private sealed class DashboardLayoutScreen : Screen
    {
        public int RoomCount => 123456;
        public int AvailableRoomCount => 123456;
        public int OccupiedRoomCount => 123456;
        public int ReservedRoomCount => 123456;
        public int TodayCheckInCount => 123456;
        public int TodayCheckOutCount => 123456;
        public int GuestCount => int.MaxValue;
        private decimal totalRevenue = 1234567.89m;
        public decimal TotalRevenue
        {
            get => totalRevenue;
            set { totalRevenue = value; NotifyOfPropertyChange(); }
        }
        private ObservableCollection<Grid42Row> reservations = Grid42Rows(), rooms = Grid42Rows();
        public ObservableCollection<Grid42Row> Reservations
        {
            get => reservations;
            set { reservations = value; NotifyOfPropertyChange(); }
        }
        public ObservableCollection<Grid42Row> Rooms
        {
            get => rooms;
            set { rooms = value; NotifyOfPropertyChange(); }
        }
    }

    private static async Task DashboardLayoutChecks(Ui ui)
    {
        var fixture = await ui.Call(() =>
        {
            // Real compiled administrative navigation + Dashboard, without production VM constructors.
            var owner = RuntimeHelpers.GetUninitializedObject(typeof(AdminViewModel));
            var model = new DashboardLayoutScreen { Parent = owner };
            var dashboard = new DashboardView { DataContext = model };
            var admin = new AdminView { DataContext = owner };
            ((ContentControl)admin.FindName("ActiveItem")).Content = dashboard;
            var host = new Border { Child = admin };
            var hwnd = new HwndSource(new HwndSourceParameters("Offline Admin Dashboard layout")
            { Width = 2240, Height = 1000, WindowStyle = unchecked((int)0x80000000) });
            hwnd.RootVisual = host;
            return (host, hwnd, admin, dashboard, model);
        });
        var (host, hwnd, admin, view, vm) = fixture;
        try
        {
            await SetLayout(ui, host, 2240, 1000);
            var grids = await ui.Call(() => Visuals<DataGrid>(view).ToArray());
            var cards = await ui.Call(() => ((WrapPanel)view.FindName("MetricCards")).Children.Cast<Border>().ToArray());
            var page = await ui.Call(() => (ScrollViewer)view.FindName("PageScroller"));
            var revenue = await ui.Call(() => (TextBlock)view.FindName("RevenueValue"));
            await ui.Call(() =>
            {
                DashCheck(grids.Length == 2 && cards.Length == 8, "original tables and indicators only");
                DashCheck(grids.Select(g => g.Columns.Count).SequenceEqual(new[] { 8, 5 }), "original columns");
                var expectedColors = new[] { "#FF2E5A92", "#FF008000", "#FFD90000", "#FFB8860B", "#FF0E75C7", "#FF6A0DAD", "#FF0B6B59", "#FF2B2B2B" };
                DashCheck(cards.Select(c => c.Background.ToString()).SequenceEqual(expectedColors), "all original card colors");
                DashCheck(!Visuals<Viewbox>(view).Any(), "no downscaling of numbers");
                foreach (var grid in grids)
                {
                    DashCheck(grid.Style == Application.Current.FindResource("StandardResizableDataGridStyle") && grid.RowHeaderWidth == 40,
                        "existing shared grid style and row headers");
                    grid.SelectedIndex = 4;
                    CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Descending));
                }
                ResizeThroughNativeGripper(grids[0], 0, -56);
                ResizeThroughNativeGripper(grids[1], 0, 72);
                return true;
            });
            var selected = await ui.Call(() => grids.Select(g => g.SelectedItem).ToArray());
            var widths = await ui.Call(() => grids.Select(g => g.Columns.Select(c => c.Width).ToArray()).ToArray());
            foreach (var (width, height, columnCount, sideBySide) in new[]
            {
                (2240d, 1000d, 8, true), (1680d, 900d, 8, false), (1200d, 800d, 8, false),
                (1000d, 740d, 4, false), (800d, 600d, 4, false), (600d, 500d, 3, false),
                (1200d, 320d, 8, false), (1200d, 1000d, 8, false), (2240d, 1000d, 8, true)
            })
            {
                await ui.Call(() => { page.ScrollToTop(); return true; });
                await SetLayout(ui, host, width, height);
                await ui.Call(() =>
                {
                    var metrics = (WrapPanel)view.FindName("MetricCards");
                    DashCheck(Math.Abs(view.ActualWidth - (width - (admin.IsCompactNavigation ? 0 : 240))) < 1, "actual content accounts for adaptive navigation");
                    DashCheck(view.MetricColumnCount == columnCount && view.AreTablesSideBySide == sideBySide, "adaptive mode at " + width);
                    DashCheck(Visuals<DataGrid>(view).SequenceEqual(grids), "no re-created tables on resize");
                    var tableA = (Grid)view.FindName("ReservationsPanel");
                    var tableB = (Grid)view.FindName("RoomsPanel");
                    var positionA = tableA.TranslatePoint(new Point(), view);
                    var positionB = tableB.TranslatePoint(new Point(), view);
                    DashCheck(sideBySide ? positionB.X >= positionA.X + tableA.ActualWidth : positionB.Y >= positionA.Y + tableA.ActualHeight,
                        "table panels never overlap");
                    var rectangles = cards.Select(c => new Rect(c.TranslatePoint(new Point(), metrics), c.RenderSize)).ToArray();
                    for (var i = 0; i < cards.Length; i++)
                    {
                        DashCheck(rectangles[i].Left >= 0 && rectangles[i].Right <= metrics.ActualWidth + 1, "card within available width");
                        for (var j = i + 1; j < cards.Length; j++)
                            DashCheck(!rectangles[i].IntersectsWith(rectangles[j]), "cards do not overlap");
                        var label = Visuals<TextBlock>(cards[i]).Last();
                        var numberScroller = Visuals<ScrollViewer>(cards[i]).Single();
                        var number = Visuals<TextBlock>(cards[i]).First(t => t.Style == view.FindResource("ResponsiveMetricValue"));
                        var numberWidth = new FormattedText(number.Text, System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight, new Typeface(number.FontFamily, number.FontStyle, number.FontWeight, number.FontStretch),
                            number.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(number).PixelsPerDip).WidthIncludingTrailingWhitespace;
                        DashCheck(number.FontSize >= 26 && numberScroller.ExtentWidth + 1 >= numberWidth,
                            "full large values remain reachable without tiny type");
                        DashCheck(cards[i].ActualHeight <= 128, "cards remain compact instead of stretching vertically");
                        var required = new FormattedText(label.Text, System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight, new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                            label.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(label).PixelsPerDip);
                        DashCheck(label.TextWrapping == TextWrapping.WrapWithOverflow && label.ActualWidth >= required.Width,
                            "labels remain readable without mid-word breaks");
                    }
                    DashCheck(revenue.FontSize == 26 && revenue.Text.Contains("1234567"), "revenue value retained at readable font size");
                    DashCheck(Equals(revenue.ToolTip, revenue.Text), "full revenue tooltip");
                    foreach (var grid in grids)
                    {
                        var i = Array.IndexOf(grids, grid);
                        DashCheck(grid.ActualHeight is > 200 and < 1200 && grid.ActualWidth <= view.ActualWidth,
                            "finite table viewport inside page scroller");
                        DashCheck(Visuals<DataGridRow>(grid).Count() is > 0 and < 100 && grid.EnableRowVirtualization && grid.EnableColumnVirtualization,
                            "native virtualization with 600 data rows");
                        DashCheck(grid.Columns.Select(c => c.Width).SequenceEqual(widths[i]), "manual widths retained through layout changes");
                        DashCheck(ReferenceEquals(grid.SelectedItem, selected[i]) && CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Count == 1,
                            "selection and sort retained");
                    }
                    if (width <= 1000 || height <= 320) DashCheck(page.ScrollableHeight > 0, "short/narrow page scrolls vertically");
                    if (width == 1200 && height == 1000) DashCheck(page.ScrollableHeight < 1, "no unnecessary page scrolling with sufficient height");
                    page.ScrollToBottom();
                    Console.WriteLine($"DASH {width}x{height}: content={view.ActualWidth:F0}, cards={view.MetricColumnCount} columns, sideBySide={sideBySide}, pageScroll={page.ScrollableHeight:F0}");
                    return true;
                });
                await SetLayout(ui, host, width, height);
                await ui.Call(() =>
                {
                    var bottom = ((Grid)view.FindName("RoomsPanel")).TranslatePoint(new Point(), page).Y +
                        ((Grid)view.FindName("RoomsPanel")).ActualHeight;
                    DashCheck(bottom <= page.ActualHeight + 1, "second table bottom accessible by page scrolling");
                    return true;
                });
            }
            foreach (var (width, expected) in new[] { (2065d, true), (2030d, false), (2065d, false), (2100d, true) })
            {
                await SetLayout(ui, host, width, 1000);
                await ui.Call(() => { DashCheck(view.AreTablesSideBySide == expected, "table breakpoint hysteresis"); return true; });
            }
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                page.ScrollToTop();
                Visuals<ScrollViewer>(grids[0]).First().ScrollToTop();
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
                Visuals<DataGridCell>(grids[0]).First().RaiseEvent(wheel);
                DashCheck(!wheel.Handled && page.VerticalOffset == 0, "table retains wheel while it can scroll");
                Visuals<ScrollViewer>(grids[0]).First().ScrollToBottom();
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
                Visuals<DataGridCell>(grids[0]).First().RaiseEvent(wheel);
                DashCheck(wheel.Handled, "wheel at end of table continues onto page");
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                DashCheck(page.VerticalOffset > 0 || SystemParameters.WheelScrollLines == 0, "page moves after table boundary");
                return true;
            });
            // Extreme amounts remain full-size and reachable, not silently reduced to tiny text.
            await ui.Call(() => { vm.TotalRevenue = decimal.MaxValue; page.ScrollToTop(); return true; });
            // Both the binding notification and the newly wrapped cards can queue another native
            // measure. Drain bounded Dispatcher turns; do not assert against an old scrollbar extent.
            for (var turn = 0; turn < 8; turn++)
            {
                await SetLayout(ui, host, 400, 500);
                if (await ui.Call(() => revenue.Text == decimal.MaxValue.ToString("F2") &&
                    Visuals<ScrollViewer>(cards.Last()).Single().ScrollableWidth > 0)) break;
            }
            await ui.Call(() =>
            {
                var scroller = Visuals<ScrollViewer>(cards.Last()).Single();
                DashCheck(revenue.FontSize == 26 && scroller.ScrollableWidth > 0,
                    $"exceptionally large amount has horizontal access instead of shrinking: text={revenue.Text}, width={scroller.ActualWidth}, viewport={scroller.ViewportWidth}, extent={scroller.ExtentWidth}, scroll={scroller.ScrollableWidth}");
                scroller.ScrollToRightEnd();
                // A value-only scroller must not trap the page wheel.
                revenue.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                return true;
            });
            await SetLayout(ui, host, 400, 500);
            await ui.Call(() =>
            {
                DashCheck(page.VerticalOffset > 0 || SystemParameters.WheelScrollLines == 0, "wheel over card scrolls page");
                DashCheck(Visuals<ScrollViewer>(cards.Last()).Single().HorizontalOffset > 0, "last revenue digits reachable");
                vm.Reservations = new(); vm.Rooms = new();
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                foreach (var grid in grids) Visuals<ScrollViewer>(grid).First().ScrollToRightEnd();
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                foreach (var grid in grids)
                {
                    var last = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns.Last());
                    var point = last.TranslatePoint(new Point(), grid);
                    DashCheck(point.X >= -1 && point.X + last.ActualWidth <= grid.ActualWidth + 1, "empty table last header reachable");
                }
                return true;
            });
        }
        finally
        {
            await ui.Call(() => { host.Child = null; hwnd.RootVisual = null; hwnd.Dispose(); return true; });
        }
        Console.WriteLine($"PASS: {dashboardLayoutAssertions} Dashboard layout assertions; compiled Admin + Dashboard, fake data, no database.");
    }
}
