using Caliburn.Micro;
using Nest.ViewModels.Admin;
using Nest.ViewModels.Reception;
using Nest.Views.Admin;
using Nest.Views.Reception;
using Nest.Views.Behaviors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

internal static partial class Program
{
    private static int remainingGridAssertions;
    private static void Grid42Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("DataGrid 4.2: " + message);
        remainingGridAssertions++;
    }

    // A presentation-only Screen: no production VM constructors, activation, repositories or database.
    private sealed class Grid42Screen : Screen
    {
        private ObservableCollection<Grid42Row> rows = new();
        public ObservableCollection<Grid42Row> Rows
        {
            get => rows;
            set
            {
                rows = value;
                foreach (var name in new[] { "Guests", "GuestHistory", "Rooms", "Users", "Services", "Events", "Reservations" })
                    NotifyOfPropertyChange(name);
            }
        }
        public ObservableCollection<Grid42Row> Guests => rows;
        public ObservableCollection<Grid42Row> GuestHistory => rows;
        public ObservableCollection<Grid42Row> Rooms => rows;
        public ObservableCollection<Grid42Row> Users => rows;
        public ObservableCollection<Grid42Row> Services => rows;
        public ObservableCollection<Grid42Row> Events => rows;
        public ObservableCollection<Grid42Row> Reservations => rows;
        public object? SelectedRoom { get; set; }
        public object? SelectedReservation { get; set; }
        public object? SelectedGuest { get; set; }
        public object? SelectedUser { get; set; }
        public object? SelectedService { get; set; }
        public object? SelectedEvent { get; set; }
    }
    private sealed class Grid42Row
    {
        public int Id { get; set; }
        public string ReservationCode { get; set; } = "BNR-DDDDDDDDDDDDDDDD";
        public string Guest { get; set; } = "Alexandra Muster";
        public string GastName => Guest;
        public string FirstName { get; set; } = "Alexandra";
        public string LastName { get; set; } = "Muster";
        public string Email { get; set; } = "guest@example.test";
        public string Phone { get; set; } = "+49 151 12345678";
        public string Nationality { get; set; } = "Deutschland";
        public string IdType { get; set; } = "Personalausweis";
        public string IdNumber { get; set; } = "L01X00T471";
        public string Room { get; set; } = "9999";
        public int RoomNumber { get; set; } = 9999;
        public string Type { get; set; } = "Executive";
        public string RoomType => Type;
        public int Capacity { get; set; } = 9;
        public decimal PricePerNight { get; set; } = 9999.99m;
        public string Description { get; set; } = "Room description with optional long text";
        public string UserName { get; set; } = "receptionist";
        public string FullName => Guest;
        public string Role { get; set; } = "Rezeptionist";
        public DateTime DateAdded { get; set; } = new(2026, 11, 13);
        public DateTime CheckInDate => DateAdded;
        public DateTime CheckOutDate => DateAdded.AddDays(1);
        public DateTime CheckOut => CheckOutDate;
        public int Nights { get; set; } = 999;
        public decimal TotalAmount { get; set; } = 99999.99m;
        public decimal Amount => TotalAmount;
        public string Status { get; set; } = "Ausgecheckt";
        public string ServiceName { get; set; } = "Frühstück und Getränke";
        public string CategoryDisplay { get; set; } = "Speisen und Getränke";
        public decimal Price { get; set; } = 9999.99m;
        public string Title { get; set; } = "Hotelveranstaltung";
        public DateTime EventDate => DateAdded;
        public TimeSpan EventTime { get; set; } = new(18, 30, 0);
        public string Location { get; set; } = "Konferenzsaal";
        public decimal TicketPrice { get; set; } = 9999.99m;
        public int MaxTickets { get; set; } = 9999;
    }
    private static ObservableCollection<Grid42Row> Grid42Rows() => new(
        Enumerable.Range(0, 600).Select(i => new Grid42Row { Id = i + 1 }));

    private static void Grid42Measure(DataGrid grid)
    {
        foreach (var column in grid.Columns)
        {
            var header = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == column);
            var cell = Visuals<DataGridCell>(grid).First(c => c.Column == column);
            var text = Visuals<TextBlock>(cell).First();
            double Width(string value, Typeface face, double size) => new FormattedText(value,
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size,
                Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip).WidthIncludingTrailingWhitespace;
            var headerRequired = Math.Ceiling(Width(column.Header.ToString()!,
                new Typeface(header.FontFamily, header.FontStyle, header.FontWeight, header.FontStretch), header.FontSize)
                + header.Padding.Left + header.Padding.Right + 22);
            var required = Math.Ceiling(Width(text.Text,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize)
                + cell.Padding.Left + cell.Padding.Right + text.Margin.Left + text.Margin.Right + 4);
            Grid42Check(column.MinWidth >= headerRequired, $"{DataGridColumnWidthSession.GetTableKey(grid)}/{column.Header}: initial header needs {headerRequired}, has {column.MinWidth}");
            // Free text has no finite maximum; minimums are measured against representative data, not arbitrary prose.
            if (column.SortMemberPath is not ("Description" or "ServiceName" or "CategoryDisplay" or "Title" or "Location" or "Email"))
                Grid42Check(column.MinWidth >= required, $"{column.Header}: representative value needs {required}, has {column.MinWidth}");
            Grid42Check(Equals(text.ToolTip, text.Text) && Equals(header.ToolTip, header.Content), "full value/header tooltip");
            Grid42Check(cell.Padding == new Thickness(6, 0, 6, 0), "shared cell padding");
            Console.WriteLine($"GRID42 {DataGridColumnWidthSession.GetTableKey(grid)} / {column.Header}: initial={column.Width}, minimum={column.MinWidth}, measured header={headerRequired}, sample={required}");
        }
    }

    private static async Task RemainingGridChecks(Ui ui)
    {
        var cases = new (Func<UserControl> Create, int[] Columns)[]
        {
            (() => new GastManagementView(), new[] { 8, 8 }),
            (() => new ZimmerView(), new[] { 6 }),
            (() => new BenutzerView(), new[] { 5 }),
            (() => new DienstleistungView(), new[] { 4 }),
            (() => new EventView(), new[] { 7 }),
            (() => new DashboardView(), new[] { 8, 5 }),
            (() => new ReservationManagementView(), new[] { 8 })
        };
        foreach (var (create, columns) in cases)
        {
            var fixture = await ui.Call(() =>
            {
                var vm = new Grid42Screen { Parent = RuntimeHelpers.GetUninitializedObject(typeof(AdminViewModel)), Rows = Grid42Rows() };
                var view = create(); view.DataContext = vm;
                var host = new Border { Child = view };
                var hwnd = new HwndSource(new HwndSourceParameters("Offline grid 4.2")
                { Width = 4400, Height = 900, WindowStyle = unchecked((int)0x80000000) });
                hwnd.RootVisual = host;
                return (vm, view, host, hwnd);
            });
            var (vm, view, host, hwnd) = fixture;
            try
            {
                await SetLayout(ui, host, 4400, 900);
                var grids = await ui.Call(() => Visuals<DataGrid>(view).ToArray());
                var original = await ui.Call(() =>
                {
                    Grid42Check(grids.Select(g => g.Columns.Count).SequenceEqual(columns), "real compiled column counts " + view.GetType().Name);
                    Grid42Check(grids.Select(DataGridColumnWidthSession.GetTableKey).Distinct().Count() == grids.Length, "distinct table keys");
                    foreach (var grid in grids)
                    {
                        Grid42Check(grid.Style == Application.Current.FindResource("StandardResizableDataGridStyle"), "shared style");
                        Grid42Check(grid.IsReadOnly == (view is GastManagementView or ZimmerView or BenutzerView or ReservationManagementView),
                            "existing read-only/editable mode preserved");
                        Grid42Check(grid.Columns.All(c => c is DataGridTextColumn && c.CanUserResize && c.CanUserSort), "original text columns/sorting/resizing");
                        Grid42Check(grid.RowHeaderWidth == 40 && grid.CanUserResizeColumns && grid.CanUserSortColumns, "native header/resize/sort");
                        Grid42Measure(grid);
                        Grid42Check(Visuals<DataGridRow>(grid).Count() is > 0 and < 100 && grid.EnableRowVirtualization && grid.EnableColumnVirtualization,
                            "populated native virtualization");
                    }
                    return grids.Select(g => g.Columns.Select(c => c.Width).ToArray()).ToArray();
                });
                foreach (var populated in new[] { true, false })
                {
                    await ui.Call(() => { vm.Rows = populated ? Grid42Rows() : new(); return true; });
                    foreach (var width in new[] { 4400d, 800d, 600d })
                    {
                        await SetLayout(ui, host, width, 700);
                        await ui.Call(() =>
                        {
                            foreach (var grid in grids)
                            {
                                var scroll = Visuals<ScrollViewer>(grid).First();
                                Grid42Check(grid.ActualWidth > 0 && grid.ActualWidth <= width && grid.ActualHeight > 30, "bounded grid viewport");
                                // The native extent includes the realized row-header gutter as well as columns.
                                var overflow = grid.Columns.Sum(c => c.ActualWidth) + grid.CellsPanelHorizontalOffset > scroll.ViewportWidth + 1;
                                Grid42Check(overflow ? scroll.ScrollableWidth > 0 : scroll.ScrollableWidth < 1,
                                    $"appropriate scroll extent: {DataGridColumnWidthSession.GetTableKey(grid)}, empty={!populated}, window={width}, columns={grid.Columns.Sum(c => c.ActualWidth)}, viewport={scroll.ViewportWidth}, extent={scroll.ExtentWidth}, scrollable={scroll.ScrollableWidth}");
                                scroll.ScrollToRightEnd();
                            }
                            if (view is GastManagementView)
                                foreach (var button in Visuals<Button>(view).Where(b => b.Name is "SearchGuests" or "ClearSearch" or "Refresh"))
                                {
                                    var point = button.TranslatePoint(new Point(), view);
                                    Grid42Check(point.X >= 0 && point.X + button.ActualWidth <= width, "wrapped guest toolbar");
                                }
                            return true;
                        });
                        await SetLayout(ui, host, width, 700);
                        await ui.Call(() =>
                        {
                            foreach (var grid in grids)
                            {
                                var header = Visuals<DataGridColumnHeader>(grid).First(h => h.Column == grid.Columns.Last());
                                var point = header.TranslatePoint(new Point(), grid);
                                Grid42Check(point.X >= -1 && point.X + header.ActualWidth <= grid.ActualWidth + 1,
                                    $"last header reachable: {DataGridColumnWidthSession.GetTableKey(grid)}, populated={populated}, window={width}, x={point.X}, header={header.ActualWidth}, grid={grid.ActualWidth}, offset={Visuals<ScrollViewer>(grid).First().HorizontalOffset}");
                            }
                            return true;
                        });
                    }
                }
                await ui.Call(() => { vm.Rows = Grid42Rows(); return true; });
                await SetLayout(ui, host, 4400, 900);
                await ui.Call(() =>
                {
                    foreach (var grid in grids)
                    {
                        var scroll = Visuals<ScrollViewer>(grid).First(); scroll.ScrollToLeftEnd();
                    }
                    return true;
                });
                await SetLayout(ui, host, 4400, 900);
                await ui.Call(() =>
                {
                    foreach (var grid in grids)
                    {
                        // Exercise every column's actual WPF resize gripper, not just a property setter.
                        for (var i = 0; i < grid.Columns.Count; i++)
                        {
                            ResizeThroughNativeGripper(grid, i, 44 - grid.Columns[i].ActualWidth);
                            Grid42Check(grid.Columns[i].MinWidth == 44 && Math.Abs(grid.Columns[i].ActualWidth - 44) < 1, "shrink to technical minimum");
                        }
                        ResizeThroughNativeGripper(grid, 0, 120);
                        grid.SelectedIndex = 2;
                        CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Clear();
                        CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Descending));
                        Grid42Check(grid.SelectedItem != null && CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.Count == 1, "selection/sort");
                    }
                    return true;
                });
                var manual = await ui.Call(() => grids.Select(g => g.Columns.Select(c => c.Width).ToArray()).ToArray());
                await ui.Call(() => { vm.Rows = Grid42Rows(); return true; });
                await SetLayout(ui, host, 800, 700);
                await ui.Call(() =>
                {
                    Grid42Check(grids.Select((g, i) => g.Columns.Select(c => c.Width).SequenceEqual(manual[i])).All(x => x), "data refresh and resize preserve manual widths");
                    host.Child = new TextBlock { Text = "Another page" }; return true;
                });
                await SetLayout(ui, host, 800, 700);
                await ui.Call(() => { view = create(); view.DataContext = vm; host.Child = view; return true; });
                await SetLayout(ui, host, 800, 700);
                await ui.Call(() =>
                {
                    grids = Visuals<DataGrid>(view).ToArray();
                    Grid42Check(grids.Select((g, i) => g.Columns.Select(c => c.Width).SequenceEqual(manual[i])).All(x => x), "fresh View navigation restores same session");
                    host.Child = null;
                    return true;
                });
                await SetLayout(ui, host, 800, 700);
                foreach (var role in new[] { typeof(AdminViewModel), typeof(ReceptionViewModel) })
                {
                    await ui.Call(() =>
                    {
                        vm.Parent = RuntimeHelpers.GetUninitializedObject(role);
                        view = create(); view.DataContext = vm; host.Child = view; return true;
                    });
                    await SetLayout(ui, host, 4400, 900);
                    await ui.Call(() =>
                    {
                        grids = Visuals<DataGrid>(view).ToArray();
                        // DataGridLength equality also compares transient desired/display doubles.
                        // Assert the exact XAML unit/value and visible width with DIP rounding tolerance.
                        Grid42Check(grids.Select((g, i) => g.Columns.Select((c, j) =>
                            c.Width.UnitType == original[i][j].UnitType && c.Width.Value == original[i][j].Value &&
                            Math.Abs(c.ActualWidth - original[i][j].DisplayValue) < 1).All(x => x)).All(x => x),
                            "new authenticated session restores XAML defaults: " + view.GetType().Name);
                        host.Child = null; return true;
                    });
                    await SetLayout(ui, host, 800, 700);
                }
            }
            finally
            {
                await ui.Call(() => { host.Child = null; hwnd.RootVisual = null; hwnd.Dispose(); return true; });
            }
        }
        Console.WriteLine($"PASS: {remainingGridAssertions} remaining DataGrid assertions; nine compiled grids, fake data, no SQL.");
    }
}
