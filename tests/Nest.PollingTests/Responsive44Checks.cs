using Caliburn.Micro;
using Nest.ViewModels.Admin;
using Nest.Views.Admin;
using Nest.Views.Reception;
using Nest.Views.CustomerPortal;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

internal static partial class Program
{
    private static int responsive44Assertions;
    private static void R44(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Responsive 4.4: " + message);
        responsive44Assertions++;
    }
    private static readonly (double Width, double Height)[] Sizes44 =
    { (1680, 900), (1200, 800), (1000, 740), (800, 600), (600, 500), (600, 320), (960, 600), (940, 600), (980, 600), (1680, 900) };

    private sealed class Crud44Screen : Screen
    {
        public ObservableCollection<Grid42Row> Rows { get; set; } = Grid42Rows();
        public ObservableCollection<Grid42Row> Rooms => Rows;
        public ObservableCollection<Grid42Row> Users => Rows;
        public ObservableCollection<Grid42Row> Services => Rows;
        public ObservableCollection<Grid42Row> Events => Rows;
        public object? SelectedRoom { get; set; }
        public object? SelectedUser { get; set; }
        public object? SelectedService { get; set; }
        public object? SelectedEvent { get; set; }
        public int RoomNumber { get; set; } = 101;
        public string UserName { get; set; } = "draft user";
        public string ServiceName { get; set; } = "draft service";
        public string Title { get; set; } = "draft event";
        public List<string> Calls { get; } = new();
        public void Hinzufügen() => Calls.Add("Add");
        public void Aktualisieren() => Calls.Add("Update");
        public void Löschen() => Calls.Add("Delete");
        public void ClearForm() => Calls.Add("Clear");
    }
    private sealed class Reception44Screen : Screen
    {
        public string PageTitle => "Alle Reservierungen & Aktivitäten";
        public string SelectedSection => "AllBookings";
        public required Action<string> Navigate { get; init; }
        public required System.Action LogOut { get; init; }
        public List<string> Calls { get; } = new();
        private void Go(string name) { Calls.Add(name); Navigate(name); }
        public void ShowReservations() => Go("Reservations");
        public void ShowGuests() => Go("Guests");
        public void ShowAllReservations() => Go("AllBookings");
        public void ShowCustomerPortal() => Go("CustomerPortal");
        public void Logout() { Calls.Add("Logout"); LogOut(); }
        public void Exit() => Calls.Add("Exit");
    }
    private sealed class Portal44Screen
    {
        public bool ShowPortalHeader => false;
        public Portal44Screen ZimmerBuchung => this;
        public Portal44Screen OnlineZahlung => this;
        public string Vorname { get; set; } = "Unfinished name";
        public string Nachname { get; set; } = "Muster";
        public string Nationalitaet { get; set; } = "Deutschland";
        public string Email { get; set; } = "guest@example.test";
        public string Telefon { get; set; } = "+49 123456789";
        public int Erwachsene { get; set; } = 2;
        public int Kinder { get; set; } = 1;
        public string Reservierungscode { get; set; } = "BNR-DDDDDDDDDDDDDDDD";
        public string LetzterPdfPfadText => "Offline example, no document opened";
    }
    private static HwndSource Host44(Border host)
    {
        var source = new HwndSource(new HwndSourceParameters("Offline 4.4 real compiled Views")
        { Width = 1680, Height = 900, WindowStyle = unchecked((int)0x80000000) });
        source.RootVisual = host;
        return source;
    }
    private static void Click44(FrameworkElement view, string name) =>
        ((Button)view.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static async Task Responsive44Checks(Ui ui)
    {
        await ui.Call(() =>
        {
            if (!Application.Current.Resources.Contains("PageHeaderBrush"))
                foreach (var resource in new[] { "Palette", "CommonStyles", "ReceptionFormStyles", "DataGridStyles", "ButtonStyles" })
                    Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri("pack://application:,,,/Nest;component/Styles/" + resource + ".xaml") });
            return true;
        });
        await Crud44Checks(ui);
        Console.WriteLine("PASS 4.4 CRUD layouts/drafts/commands");
        await Reception44Checks(ui);
        Console.WriteLine("PASS 4.4 reception drawer/headers/navigation");
        await Portal44Checks(ui);
        Console.WriteLine($"PASS: {responsive44Assertions} responsive 4.4 assertions; compiled Views, fake VMs, hidden STA hosts, no SQL.");
    }

    private static async Task Crud44Checks(Ui ui)
    {
        foreach (var create in new Func<UserControl>[] { () => new ZimmerView(), () => new BenutzerView(), () => new DienstleistungView(), () => new EventView() })
        {
            var fixture = await ui.Call(() =>
            {
                var view = create();
                var vm = new Crud44Screen { Parent = RuntimeHelpers.GetUninitializedObject(typeof(AdminViewModel)) };
                ViewModelBinder.Bind(vm, view, null);
                var admin = new AdminView();
                ((ContentControl)admin.FindName("ActiveItem")).Content = view;
                var host = new Border { Child = admin };
                return (view, vm, admin, host, hwnd: Host44(host));
            });
            var (view, vm, admin, host, hwnd) = fixture;
            try
            {
                await SetLayout(ui, host, 1680, 900);
                var state = await ui.Call(() =>
                {
                    var table = (DataGrid)view.FindName("CrudGrid");
                    table.SelectedIndex = 3;
                    ResizeThroughNativeGripper(table, 0, 30);
                    var collection = CollectionViewSource.GetDefaultView(table.ItemsSource);
                    collection.SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Descending));
                    var form = (ScrollViewer)view.FindName("CrudForm");
                    var input = Visuals<TextBox>(form).First(t => t.GetBindingExpression(TextBox.TextProperty) != null);
                    input.Text = "unsaved invalid input !";
                    return (table, form, input, selected: table.SelectedItem, columns: table.Columns.Select(c => c.Width).ToArray());
                });
                foreach (var (width, height) in Sizes44)
                {
                    // Also reserve a conservative non-client window frame, unlike isolated-view tests.
                    await SetLayout(ui, host, width - 16, height - 40);
                    await ui.Call(() =>
                    {
                        var compact = ((FrameworkElement)view.FindName("CrudPaneChoices")).IsVisible;
                        if (width >= 1200) R44(!compact && state.form.IsVisible && state.table.IsVisible, "wide keeps both panes " + view.GetType().Name);
                        if (width <= 800)
                        {
                            R44(compact && state.table.IsVisible && !state.form.IsVisible, "compact initially shows list");
                            R44(state.table.ActualWidth > view.ActualWidth - 2, "list receives full width");
                            Click44(view, "OpenEditForm");
                        }
                        return true;
                    });
                    await SetLayout(ui, host, width - 16, height - 40);
                    await ui.Call(() =>
                    {
                        if (width <= 800)
                        {
                            R44(state.form.IsVisible && !state.table.IsVisible, "edit opens the original form");
                            state.form.ScrollToBottom();
                        }
                        R44(state.input.Text == "unsaved invalid input !", "raw invalid draft survives resize/switch");
                        R44(ReferenceEquals(state.selected, state.table.SelectedItem), "selection identity preserved");
                        R44(state.table.Columns.Select(c => c.Width).SequenceEqual(state.columns), "manual widths unchanged");
                        R44(CollectionViewSource.GetDefaultView(state.table.ItemsSource).SortDescriptions.Count == 1, "sort unchanged");
                        R44(vm.Calls.Count == 0, "presentation never writes or clears");
                        return true;
                    });
                    await SetLayout(ui, host, width - 16, height - 40);
                if (width <= 800)
                {
                    // At low height the two action rows need not be visible together.
                    // Verify each real button can be brought into the bounded viewport.
                    foreach (var name in new[] { "Hinzufügen", "Aktualisieren", "Löschen", "ClearForm" })
                    {
                        await ui.Call(() => { ((Button)view.FindName(name)).BringIntoView(); return true; });
                        await SetLayout(ui, host, width - 16, height - 40);
                        await ui.Call(() =>
                        {
                            var button = (Button)view.FindName(name);
                            var point = button.TranslatePoint(new Point(), state.form);
                            R44(point.Y >= -1 && point.Y + button.ActualHeight <= state.form.ActualHeight + 1,
                                $"{view.GetType().Name} {width}x{height}: action {name}, y={point.Y}, height={button.ActualHeight}, viewport={state.form.ActualHeight}");
                            return true;
                        });
                    }
                    await ui.Call(() => { ((RadioButton)view.FindName("CrudListChoice")).IsChecked = true; return true; });
                }
                }
                await SetLayout(ui, host, 800, 600);
                await ui.Call(() => { Click44(view, "OpenNewForm"); return true; });
                await SetLayout(ui, host, 800, 600);
                await ui.Call(() =>
                {
                    R44(state.form.IsVisible && state.input.Text == "unsaved invalid input !", "new-entry shortcut retains unfinished draft");
                    foreach (var name in new[] { "Hinzufügen", "Aktualisieren", "Löschen", "ClearForm" }) Click44(view, name);
                    R44(vm.Calls.SequenceEqual(new[] { "Add", "Update", "Delete", "Clear" }), "original commands bound exactly once");
                    ((ContentControl)admin.FindName("ActiveItem")).Content = new TextBlock { Text = "Another page" };
                    return true;
                });
                await SetLayout(ui, host, 800, 600);
                await ui.Call(() => { ((ContentControl)admin.FindName("ActiveItem")).Content = view; return true; });
                await SetLayout(ui, host, 800, 600);
                await ui.Call(() =>
                {
                    R44(state.input.Text == "unsaved invalid input !", "same-view reactivation preserves draft");
                    R44(state.table.Columns.Select(c => c.Width).SequenceEqual(state.columns), "reactivation preserves widths");
                    return true;
                });
            }
            finally { await ui.Call(() => { hwnd.RootVisual = null; hwnd.Dispose(); return true; }); }
        }
    }

    private static async Task Reception44Checks(Ui ui)
    {
        var fixture = await ui.Call(() =>
        {
            var view = new ReceptionView();
            var page = new GastManagementView { DataContext = new Grid42Screen { Rows = Grid42Rows() } };
            var content = (ContentControl)view.FindName("ActiveItem");
            content.Content = page;
            var host = new Border { Child = view };
            var vm = new Reception44Screen { Navigate = s => content.Content = new TextBlock { Text = s }, LogOut = () => host.Child = new TextBlock { Text = "Login" } };
            ViewModelBinder.Bind(vm, view, null);
            return (view, page, host, vm, hwnd: Host44(host));
        });
        var (view, page, host, vm, hwnd) = fixture;
        try
        {
            foreach (var (width, height) in Sizes44)
            {
                await SetLayout(ui, host, width - 16, height - 40);
                var gridWidths = await ui.Call(() => Visuals<DataGrid>(page).Select(g => g.Columns.Select(c => c.ActualWidth).ToArray()).ToArray());
                var originalFocus = await ui.Call(() => Keyboard.FocusedElement);
                await ui.Call(() =>
                {
                    var compact = width < 1400;
                    R44(view.IsCompactNavigation == compact, "responsive reception navigation");
                    R44(Math.Abs(page.ActualWidth - (width - 16 - (compact ? 0 : 240))) < 1, "released width reaches real child View");
                    var heading = (TextBlock)view.FindName("PageHeading");
                    R44(heading.TextWrapping == TextWrapping.Wrap && heading.ActualHeight >= heading.FontSize, "readable auto-height heading");
                    if (compact) Click44(view, "OpenNavigation");
                    return true;
                });
                await SetLayout(ui, host, width - 16, height - 40);
                await ui.Call(() =>
                {
                    if (view.IsCompactNavigation)
                    {
                        R44(view.IsNavigationOpen && !((Grid)view.FindName("ContentHost")).IsEnabled, "overlay blocks background keyboard input");
                        R44(KeyboardNavigation.GetTabNavigation((Border)view.FindName("SidebarPanel")) == KeyboardNavigationMode.Cycle, "Tab/Shift+Tab constrained to open drawer");
                        R44(Math.Abs(page.ActualWidth - width + 16) < 1, "opening does not resize child");
                    }
                    R44(Visuals<DataGrid>(page).Select((g, i) => g.Columns.Select((c, j) => Math.Abs(c.ActualWidth - gridWidths[i][j]) < 1).All(x => x)).All(x => x), "drawer does not resize actual columns");
                    R44(ReferenceEquals(originalFocus, Keyboard.FocusedElement), "hidden host never steals foreground focus");
                    ((ScrollViewer)view.FindName("MenuScroller")).ScrollToBottom();
                    return true;
                });
                await SetLayout(ui, host, width - 16, height - 40);
                await ui.Call(() =>
                {
                    var scroller = (ScrollViewer)view.FindName("MenuScroller");
                    var customer = (Button)view.FindName("ShowCustomerPortal");
                    var point = customer.TranslatePoint(new Point(), scroller);
                    R44(point.Y >= -1 && point.Y + customer.ActualHeight <= scroller.ActualHeight + 1, "Kundenportal reachable by menu scroll");
                    var logout = (Button)view.FindName("Logout");
                    var logoutPoint = logout.TranslatePoint(new Point(), view);
                    var scrollPoint = scroller.TranslatePoint(new Point(), view);
                    R44(scrollPoint.Y + scroller.ActualHeight <= logoutPoint.Y + 1, "menu cannot overlap logout");
                    var exit = (Button)view.FindName("Exit");
                    R44(exit.TranslatePoint(new Point(), view).Y + exit.ActualHeight <= view.ActualHeight + 1, "exit stays inside window");
                    if (view.IsNavigationOpen) Click44(view, "CloseNavigation");
                    return true;
                });
            }
            await SetLayout(ui, host, 600, 500);
            foreach (var mode in new[] { "button", "scrim", "escape", "Reservations", "Guests", "AllBookings", "CustomerPortal" })
            {
                var done = Signal();
                var panel = await ui.Call(() => (Border)view.FindName("SidebarPanel"));
                var descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border));
                EventHandler handler = (_, _) => { if (panel.Visibility == Visibility.Collapsed) done.TrySetResult(); };
                await ui.Call(() =>
                {
                    Click44(view, "OpenNavigation");
                    descriptor.AddValueChanged(panel, handler);
                    if (mode == "button") Click44(view, "CloseNavigation");
                    else if (mode == "scrim") ((Border)view.FindName("NavigationScrim")).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                    else if (mode == "escape") panel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, hwnd, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    else Click44(view, mode == "Reservations" ? "ShowReservations" : mode == "Guests" ? "ShowGuests" : mode == "AllBookings" ? "ShowAllReservations" : "ShowCustomerPortal");
                    if (panel.Visibility == Visibility.Collapsed) done.TrySetResult();
                    return true;
                });
                try { await Bounded(done.Task); }
                finally { await ui.Call(() => { descriptor.RemoveValueChanged(panel, handler); return true; }); }
                await ui.Call(() => { R44(!view.IsNavigationOpen && ((Grid)view.FindName("ContentHost")).IsEnabled, "drawer closed by " + mode); return true; });
            }
            await ui.Call(() =>
            {
                foreach (var name in new[] { "OpenNavigation", "CloseNavigation" })
                    R44(!string.IsNullOrWhiteSpace(new ButtonAutomationPeer((Button)view.FindName(name)).GetName()), "accessible menu button");
                Click44(view, "Exit"); Click44(view, "OpenNavigation"); Click44(view, "Logout");
                R44(vm.Calls.TakeLast(2).SequenceEqual(new[] { "Exit", "Logout" }), "original logout/exit commands");
                return true;
            });
            await SetLayout(ui, host, 600, 500);
            await ui.Call(() => { R44(!view.IsLoaded && !view.IsNavigationOpen, "logout unload cancels drawer work"); return true; });
        }
        finally { await ui.Call(() => { hwnd.RootVisual = null; hwnd.Dispose(); return true; }); }
    }

    private static async Task Portal44Checks(Ui ui)
    {
        var fixture = await ui.Call(() =>
        {
            var view = new CustomerPortalView { DataContext = new Portal44Screen() };
            var reception = new ReceptionView();
            ((ContentControl)reception.FindName("ActiveItem")).Content = view;
            var host = new Border { Child = reception };
            return (view, reception, host, hwnd: Host44(host));
        });
        var (view, reception, host, hwnd) = fixture;
        try
        {
            var original = await ui.Call(() => ((Grid)view.FindName("ContactFields")).Children.Cast<StackPanel>().Select(p => (TextBox)p.Children[0]).ToArray());
            var invalidPeopleInputSet = false;
            foreach (var (width, height) in Sizes44.Concat(new[] { (Width: 380d, Height: 500d) }))
            {
                await SetLayout(ui, host, width - 16, height - 40);
                await ui.Call(() => { ((TabControl)view.FindName("PortalTabs")).SelectedIndex = 0; return true; });
                await SetLayout(ui, host, width - 16, height - 40);
                await ui.Call(() =>
                {
                    var contacts = (Grid)view.FindName("ContactFields");
                    var people = (Grid)view.FindName("PeopleFields");
                    var adultInput = Visuals<TextBox>(people.Children[0]).First(t => t.GetBindingExpression(TextBox.TextProperty) != null);
                    if (!invalidPeopleInputSet) { adultInput.Text = "invalid integer"; invalidPeopleInputSet = true; }
                    R44(adultInput.Text == "invalid integer", "unconverted numeric draft preserved across layout and tabs");
                    R44(original.SequenceEqual(contacts.Children.Cast<StackPanel>().Select(p => (TextBox)p.Children[0])), "same contact controls across resizes");
                    if (contacts.ActualWidth < 570) R44(Grid.GetRow(contacts.Children[4]) == 4 && Grid.GetColumn(contacts.Children[4]) == 0, "contacts use one column");
                    if (contacts.ActualWidth > 630) R44(Grid.GetRow(contacts.Children[4]) == 1 && Grid.GetColumn(contacts.Children[4]) == 1, "wide contacts restored");
                    foreach (var input in original)
                    {
                        var label = Visuals<TextBlock>(input).First(t => t.Text == input.Tag.ToString());
                        var required = new FormattedText(label.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize,
                            Brushes.Black, VisualTreeHelper.GetDpi(label).PixelsPerDip).WidthIncludingTrailingWhitespace;
                        R44(label.ActualWidth + 1 >= required, "contact label not clipped: " + label.Text);
                        R44(input.Text.Length > 0 && input.TranslatePoint(new Point(), view).X + input.ActualWidth <= view.ActualWidth + 1, "contact value/bounds preserved");
                    }
                    if (people.ActualWidth < 390) R44(Grid.GetRow(people.Children[1]) == 1, "Kinder below Erwachsene");
                    foreach (var field in people.Children.Cast<FrameworkElement>())
                        R44(field.TranslatePoint(new Point(), view).X + field.ActualWidth <= view.ActualWidth + 1,
                            $"{width}x{height}: people field at {field.TranslatePoint(new Point(), view).X}, width={field.ActualWidth}, page={view.ActualWidth}, people={people.ActualWidth}");
                    ((TabControl)view.FindName("PortalTabs")).SelectedIndex = 5;
                    return true;
                });
                await SetLayout(ui, host, width - 16, height - 40);
                await ui.Call(() =>
                {
                    foreach (var (gridName, buttonName) in new[] { ("ReservationCodeFields", "LoadReservationButton"), ("PdfFields", "OpenPdfButton") })
                    {
                        var grid = (Grid)view.FindName(gridName);
                        var button = (Button)view.FindName(buttonName);
                        if (grid.ActualWidth < 530) R44(Grid.GetRow(button) == 1 && Grid.GetColumn(button) == 0, "payment action below field");
                        if (grid.ActualWidth > 590) R44(Grid.GetRow(button) == 0 && Grid.GetColumn(button) == 1, "wide payment layout retained");
                        R44(button.ActualWidth > 190 && button.TranslatePoint(new Point(), view).X + button.ActualWidth <= view.ActualWidth + 1, "payment button readable and bounded");
                    }
                    var code = (StackPanel)view.FindName("ReservationCodeInput");
                    R44(code.ActualWidth >= 180, "booking-code input has useful width");
                    var input = Visuals<TextBox>(code).First();
                    R44(input.Text == "BNR-DDDDDDDDDDDDDDDD", "code preserved across tabs/layout");
                    var scroll = Visuals<ScrollViewer>(view).First(s => s.Content is Grid);
                    R44(scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled && scroll.ScrollableWidth < 1, "no page-wide horizontal scroll");
                    return true;
                });
            }
        }
        finally { await ui.Call(() => { hwnd.RootVisual = null; hwnd.Dispose(); return true; }); }
    }
}
