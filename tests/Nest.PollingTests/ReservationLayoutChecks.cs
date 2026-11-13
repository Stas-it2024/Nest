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
    private static PollingSnapshot LayoutData(string? status = null) => new(Data().Rooms,
        Enumerable.Range(0, 1000).Select(i => Data().Bookings[0] with
        { Code = i == 4 ? "BNR-3319A520FB404ADE" : "BNR-" + i.ToString("D16"), Guest = "Guest " + i,
            Status = status ?? new[] { "Reserviert", "Eingecheckt", "Ausgecheckt", "Storniert" }[i % 4] }));

    private static double CheckBookingCodeWidth(DataGrid grid)
    {
        var column = (DataGridTextColumn)grid.Columns[0];
        var cell = Visuals<DataGridCell>(grid).First(c => c.Column == column);
        var text = Visuals<TextBlock>(cell).First();
        double TextWidth(string value) => new FormattedText(value,
            System.Globalization.CultureInfo.InvariantCulture, text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip)
            .WidthIncludingTrailingWhitespace;
        // Reception generates BNR- plus 16 uppercase hexadecimal characters (20 total).
        // Cover every possible hexadecimal glyph, not just one conveniently narrow GUID.
        var widestHex = "0123456789ABCDEF".OrderByDescending(c => TextWidth(c.ToString())).First();
        var samples = new[] { "BNR-3319A520FB404ADE", "BNR-0123456789ABCDEF",
            "BNR-" + new string(widestHex, 16), "0123ABCD" }; // Guest portal: 8 hex characters.
        var inset = cell.Padding.Left + cell.Padding.Right + cell.BorderThickness.Left +
            cell.BorderThickness.Right + text.Margin.Left + text.Margin.Right;
        foreach (var sample in samples)
        {
            var required = Math.Ceiling(TextWidth(sample) + inset + 2); // Pixel-rounding reserve.
            LayoutCheck(column.MinWidth >= required, "minimum fits complete code with real font/insets: " + sample);
            LayoutCheck(column.ActualWidth >= required, "actual column fits complete code: " + sample);
            // A left-aligned TextBlock/ContentPresenter is sized to the current value.
            // The cell, minus its actual insets, bounds the text's available viewport.
            LayoutCheck(cell.ActualWidth - inset >= TextWidth(sample),
                $"realized cell fits complete code: {sample}; cell={cell.ActualWidth}; inset={inset}; text={TextWidth(sample)}");
        }
        var header = Visuals<System.Windows.Controls.Primitives.DataGridColumnHeader>(grid)
            .First(h => h.Column == column);
        var headerText = new FormattedText("Buchungsnr.", System.Globalization.CultureInfo.InvariantCulture,
            header.FlowDirection, new Typeface(header.FontFamily, header.FontStyle, header.FontWeight, header.FontStretch),
            header.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(header).PixelsPerDip);
        LayoutCheck(column.MinWidth >= headerText.WidthIncludingTrailingWhitespace +
            header.Padding.Left + header.Padding.Right + 20, "header has room for sort glyph");
        LayoutCheck(column.Width.IsStar && column.MinWidth == 220 && column.ActualWidth >= 220,
            "star allocation never shrinks protected minimum");
        LayoutCheck(text.ToolTip is string tooltip && tooltip == text.Text,
            "realized booking cell retains full-value tooltip");
        LayoutCheck(ScrollViewer.GetHorizontalScrollBarVisibility(grid) == ScrollBarVisibility.Auto,
            "horizontal overflow remains available");
        return Math.Ceiling(samples.Max(TextWidth) + inset + 2);
    }

    private static void CheckColumnMeasurements(DataGrid grid)
    {
        var values = new[] { new[] { "BNR-3319A520FB404ADE" }, new[] { "Alexandra Mustermann" },
            new[] { "9999" }, new[] { "Standard", "Deluxe", "Suite", "Executive" },
            new[] { "31.12.2026" }, new[] { "365" }, new[] { "99999,99" },
            new[] { "Reserviert", "Eingecheckt", "Ausgecheckt", "Storniert" } };
        for (var i = 0; i < grid.Columns.Count; i++)
        {
            var column = grid.Columns[i];
            var header = Visuals<System.Windows.Controls.Primitives.DataGridColumnHeader>(grid).First(h => h.Column == column);
            var cell = Visuals<DataGridCell>(grid).First(c => c.Column == column);
            var text = Visuals<TextBlock>(cell).First();
            double Width(string value, TextBlock element) => new FormattedText(value,
                System.Globalization.CultureInfo.InvariantCulture, element.FlowDirection,
                new Typeface(element.FontFamily, element.FontStyle, element.FontWeight, element.FontStretch),
                element.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(element).PixelsPerDip).WidthIncludingTrailingWhitespace;
            var title = new FormattedText(column.Header.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                header.FlowDirection, new Typeface(header.FontFamily, header.FontStyle, header.FontWeight, header.FontStretch),
                header.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(header).PixelsPerDip);
            var headerRequired = Math.Ceiling(title.WidthIncludingTrailingWhitespace + header.Padding.Left + header.Padding.Right + 20 + 2);
            var valueRequired = Math.Ceiling(values[i].Max(value => Width(value, text)) + cell.Padding.Left + cell.Padding.Right +
                cell.BorderThickness.Left + cell.BorderThickness.Right + text.Margin.Left + text.Margin.Right + 2);
            LayoutCheck(column.MinWidth >= headerRequired, "header fits with sorting indicator: " + column.Header);
            if (i != 1) // Guest names have no bounded length; keep the flexible column and tooltip.
                LayoutCheck(column.MinWidth >= valueRequired, "representative values fit: " + column.Header);
            LayoutCheck(cell.Padding == new Thickness(6, 0, 6, 0) && text.Margin.Left == 2 && text.Margin.Right == 2,
                "consistent symmetric content insets: " + column.Header);
            var originalDirection = column.SortDirection;
            try
            {
                foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
                {
                    column.SortDirection = direction;
                    header.UpdateLayout();
                    // Native themes may draw the arrow in DataGridHeaderBorder.OnRender,
                    // rather than expose a Path. Test the actual arranged title instead.
                    var titleElement = Visuals<TextBlock>(header).First(t => t.Text == column.Header.ToString());
                    LayoutCheck(titleElement.ActualWidth + 1 >= title.WidthIncludingTrailingWhitespace,
                        "sorted header title is not clipped: " + column.Header);
                    LayoutCheck(header.ActualWidth >= headerRequired,
                        "real sorted header fits full title and glyph: " + column.Header);
                }
            }
            finally { column.SortDirection = originalDirection; }
            Console.WriteLine($"COLUMN {column.Header}: headerWithSort={headerRequired}, sampleWithInsets={valueRequired}, min={column.MinWidth}, width={column.Width}");
        }
        CheckStatusCells(grid);
    }

    private static void CheckStatusCells(DataGrid grid)
    {
        var cells = Visuals<DataGridCell>(grid).Where(c => c.Column == grid.Columns[7]).ToArray();
        LayoutCheck(cells.Length > 0, "status cells realized in current viewport");
        foreach (var status in new[] { "Reserviert", "Eingecheckt", "Ausgecheckt", "Storniert" })
        {
            var text = cells.SelectMany(Visuals<TextBlock>).First(t => t.Text == status);
            var cell = cells.First(c => Visuals<TextBlock>(c).Contains(text));
            var measured = new FormattedText(status, System.Globalization.CultureInfo.InvariantCulture,
                text.FlowDirection, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip).WidthIncludingTrailingWhitespace;
            LayoutCheck(cell.ActualWidth >= measured + cell.Padding.Left + cell.Padding.Right +
                text.Margin.Left + text.Margin.Right + 2, "realized full status fits: " + status);
            LayoutCheck(text.ActualWidth + 1 >= measured, "status TextBlock not text-clipped: " + status);
        }
    }

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
                CheckColumnMeasurements(grid);
                var form = Named<ScrollViewer>(view, "FormScroller");
                var paths = new[] { "ReservationCode", "GastName", "RoomNumber", "RoomType", "CheckOut", "Nights", "Amount", "Status" };
                LayoutCheck(grid.Columns.Count == 8 && grid.Columns.Cast<DataGridTextColumn>()
                    .Select(c => ((Binding)c.Binding).Path.Path).SequenceEqual(paths), "all eight business column bindings");
                LayoutCheck(grid.Columns.Select(c => c.SortMemberPath).SequenceEqual(paths) &&
                    grid.Columns.All(c => c.CanUserSort), "all eight column sort paths");
                LayoutCheck(grid.HeadersVisibility == DataGridHeadersVisibility.All && grid.RowHeaderWidth == 40,
                    "standard left row header retained at 40 DIP");
                LayoutCheck(grid.Columns[0].Width.IsStar && grid.Columns[0].Width.Value == 1 &&
                    grid.Columns[1].Width.IsStar && grid.Columns[1].Width.Value == 2 &&
                    grid.Columns[0].MinWidth == 220 && grid.Columns[1].MinWidth == 132, "flexible columns protect complete booking codes and measured guest minimum");
                var codeText = new FormattedText("BNR-0123456789ABCDEF", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface(grid.FontFamily, grid.FontStyle, grid.FontWeight, grid.FontStretch),
                    grid.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(grid).PixelsPerDip);
                LayoutCheck(grid.Columns[0].ActualWidth >= codeText.WidthIncludingTrailingWhitespace + 12,
                    "wide mode uses spare space to display full generated booking codes");
                LayoutCheck(grid.Columns.Select(c => c.MinWidth).SequenceEqual(new double[] {220,132,88,80,104,80,104,104}) &&
                    grid.Columns.All(c => c.CanUserResize), "no column narrower than before; manual resizing retained");
                LayoutCheck(((DataGridTextColumn)grid.Columns[0]).ElementStyle.Setters.OfType<Setter>()
                    .Any(s => s.Property == FrameworkElement.ToolTipProperty && s.Value is Binding binding && binding.Path.Path == "Text"),
                    "full code/guest text available without adding trimming");
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
                        LayoutCheck(Named<TextBlock>(view, "FormTitle").Visibility == (wide ? Visibility.Visible : Visibility.Collapsed) &&
                            Named<TextBlock>(view, "ListTitle").Visibility == (wide ? Visibility.Visible : Visibility.Collapsed), "section titles only in wide mode; selector labels compact mode");
                        LayoutCheck(!Visuals<TextBlock>(listPanel).Any(t => t.Text == "Neue Reservierung"), "no creation heading inside the booking list");
                        LayoutCheck(ReferenceEquals(state.grid, Named<DataGrid>(view, "ReservationGrid")) &&
                            ReferenceEquals(state.form, Named<ScrollViewer>(view, "FormScroller")), "control identity");
                        if (wide || showForm)
                        {
                            var save = Named<Button>(view, "SaveReservation");
                            var position = save.TranslatePoint(new Point(), view);
                            LayoutCheck(position.Y >= 0 && position.Y + save.ActualHeight <= height + 1, "save within vertical bounds");
                            LayoutCheck(position.X >= 0 && position.X + save.ActualWidth <= width + 1, "save within horizontal bounds");
                            LayoutCheck(state.form.ViewportHeight > 0 && state.form.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "form viewport");
                            LayoutCheck(Visuals<TextBlock>(formPanel).Any(t => t.Text == "Gästeinformationen") &&
                                Visuals<TextBlock>(formPanel).Any(t => t.Text == "Zimmer Aufenthalt"), "useful form section headings retained");
                            var actionPanel = Named<Border>(view, "FormActionPanel");
                            LayoutCheck(ReferenceEquals(actionPanel.Child, save) && !Visuals<Button>(state.form).Contains(save) &&
                                actionPanel.BorderThickness.Top == 1 && actionPanel.Padding.Top == 12, "separated save footer outside scrolling fields");
                            var pair = Named<Grid>(view, "NamePair");
                            LayoutCheck(Grid.GetRow((UIElement)pair.Children[1]) == (narrow ? 1 : 0), "single column pairs");
                        }
                        if (wide || !showForm)
                        {
                            var realized = Visuals<DataGridRow>(state.grid).Count();
                            LayoutCheck(realized > 0 && realized < 100, "row virtualization");
                            LayoutCheck(state.grid.ActualHeight > 0 && state.grid.ActualHeight < height, "bounded table");
                            LayoutCheck(Visuals<System.Windows.Controls.Primitives.DataGridRowHeader>(state.grid)
                                .All(h => Math.Abs(h.ActualWidth - 40) < 1), "realized row headers keep original width");
                            var scroll = Visuals<ScrollViewer>(state.grid).First();
                            var available = state.grid.ActualWidth - state.grid.BorderThickness.Left - state.grid.BorderThickness.Right -
                                state.grid.RowHeaderWidth - (scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0);
                            var total = state.grid.Columns.Sum(c => c.ActualWidth);
                            var minimum = 220 + 132 + 88 + 80 + 104 + 80 + 104 + 104;
                            var codeRequired = CheckBookingCodeWidth(state.grid);
                            if (available >= minimum)
                                LayoutCheck(Math.Abs(total - available) <= 2, "flex columns consume spare width, not a ninth column");
                            else
                                LayoutCheck(total >= minimum - 1 && scroll.ScrollableWidth > 0, "horizontal scrolling, never squeeze columns below minima");
                            if (cycle == 0 && !showForm)
                                Console.WriteLine($"UI2.1 {width}x{height}: grid={state.grid.ActualWidth:F0}, columns={total:F0}, available={available:F0}, horizontalScroll={scroll.ScrollableWidth:F0}, rowHeader={state.grid.RowHeaderWidth:F0}, codeRequired={codeRequired:F0}, codeMin={state.grid.Columns[0].MinWidth:F0}");
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
                        if (!wide)
                        foreach (var name in new[] { "ListPaneChoice", "FormPaneChoice" })
                        {
                            var choice = Named<RadioButton>(view, name);
                            var selected = name == (showForm ? "FormPaneChoice" : "ListPaneChoice");
                            LayoutCheck(choice.IsChecked == selected && choice.ActualHeight >= 44 && choice.ActualWidth >= 150,
                                "selected pane and pointer target size");
                            var brush = (SolidColorBrush)choice.Background;
                            var activeBrush = (SolidColorBrush)view.FindResource("PageHeaderBrush");
                            LayoutCheck(brush.Color == (selected ? activeBrush.Color : Colors.White) &&
                                choice.FontWeight == (selected ? FontWeights.Bold : FontWeights.Normal), "visible selected/unselected style");
                            var focus = (Border)choice.Template.FindName("ChoiceFocus", choice);
                            LayoutCheck(focus.BorderThickness.Left >= 2 && choice.Template.Triggers.OfType<Trigger>()
                                .Any(t => t.Property == UIElement.IsKeyboardFocusedProperty && t.Setters.OfType<Setter>()
                                    .Any(s => s.TargetName == "ChoiceFocus" && s.Property == UIElement.VisibilityProperty && Equals(s.Value, Visibility.Visible))),
                                "explicit keyboard-focus visual retained");
                            LayoutCheck(choice.Focusable && choice.IsTabStop && new RadioButtonAutomationPeer(choice)
                                .GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider, "native radio keyboard and UIA semantics retained");
                        }
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
                listScroll.ScrollToRightEnd();
                return true;
            });
            await SetLayout(ui, host, 600, 500);
            await ui.Call(() =>
            {
                CheckStatusCells(state.grid); // Compact viewport, after scrolling to the rightmost column.
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
