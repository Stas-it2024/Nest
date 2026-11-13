using Caliburn.Micro;
using Nest.ViewModels.Admin;
using Nest.Views.Admin;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

internal static partial class Program
{
    private static int adminDrawerAssertions;
    private static void DrawerCheck(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Admin drawer: " + message);
        adminDrawerAssertions++;
    }
    private sealed class DrawerCommands : Screen
    {
        public string SelectedSection { get; private set; } = "Dashboard";
        public List<string> Calls { get; } = new();
        public required Action<string> Navigate { get; init; }
        public required System.Action Logout { get; init; }
        private void Select(string section) { Calls.Add(section); SelectedSection = section; NotifyOfPropertyChange(nameof(SelectedSection)); Navigate(section); }
        public void ShowDashboard() => Select("Dashboard");
        public void ShowZimmer() => Select("Zimmer");
        public void ShowBenutzer() => Select("Benutzer");
        public void ShowDienstleistungen() => Select("Dienstleistungen");
        public void ShowEvents() => Select("Events");
        public void ShowAlleReservierungen() => Select("AlleReservierungen");
        public void Abmelden() { Calls.Add("Abmelden"); Logout(); }
        public void Beenden() => Calls.Add("Beenden"); // Never exit the test application's Dispatcher.
    }
    private static void ClickDrawerButton(AdminView view, string name) =>
        ((Button)view.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static async Task CloseDrawerAndWait(Ui ui, AdminView view, System.Action close)
    {
        var panel = await ui.Call(() => (Border)view.FindName("SidebarPanel"));
        var done = Signal();
        var descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border));
        EventHandler handler = (_, _) => { if (panel.Visibility == Visibility.Collapsed) done.TrySetResult(); };
        try
        {
            await ui.Call(() =>
            {
                descriptor.AddValueChanged(panel, handler);
                close();
                if (panel.Visibility == Visibility.Collapsed) done.TrySetResult();
                return true;
            });
            await Bounded(done.Task); // Native animation completion/visibility, not a guessed sleep.
        }
        finally { await ui.Call(() => { descriptor.RemoveValueChanged(panel, handler); return true; }); }
    }

    private static async Task AdminDrawerChecks(Ui ui)
    {
        var fixture = await ui.Call(() =>
        {
            var admin = new AdminView();
            var model = new DashboardLayoutScreen { Parent = RuntimeHelpers.GetUninitializedObject(typeof(AdminViewModel)) };
            var dashboard = new DashboardView { DataContext = model };
            var content = (ContentControl)admin.FindName("ActiveItem");
            content.Content = dashboard;
            var host = new Border { Child = admin };
            var commands = new DrawerCommands
            {
                Navigate = section => content.Content = new TextBlock { Text = section },
                Logout = () => host.Child = new TextBlock { Text = "Login" }
            };
            // Real Caliburn convention binding, with fake commands instead of Repository-backed VMs.
            ViewModelBinder.Bind(commands, admin, null);
            var hwnd = new HwndSource(new HwndSourceParameters("Offline administrative drawer")
            { Width = 1680, Height = 900, WindowStyle = unchecked((int)0x80000000) });
            hwnd.RootVisual = host;
            return (host, admin, dashboard, commands, hwnd);
        });
        var (host, view, dashboard, commands, hwnd) = fixture;
        try
        {
            await SetLayout(ui, host, 1680, 900);
            await ui.Call(() =>
            {
                DrawerCheck(!view.IsCompactNavigation && !view.IsNavigationOpen, "wide navigation is permanent");
                DrawerCheck(((Border)view.FindName("SidebarPanel")).Visibility == Visibility.Visible, "wide panel visible");
                DrawerCheck(((Border)view.FindName("CompactToolbar")).Visibility == Visibility.Collapsed, "wide toolbar hidden");
                DrawerCheck(Math.Abs(dashboard.ActualWidth - 1440) < 1, "wide content reserves 240 DIP");
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            var widths = await ui.Call(() => Visuals<DataGrid>(dashboard).Select(g => g.Columns.Select(c => c.ActualWidth).ToArray()).ToArray());
            var beforeFocus = await ui.Call(() => Keyboard.FocusedElement);
            foreach (var closeMode in new[] { "button", "scrim", "escape" })
            {
                await ui.Call(() => { ClickDrawerButton(view, "OpenNavigation"); return true; });
                await SetLayout(ui, host, 800, 600);
                await ui.Call(() =>
                {
                    var sidebar = (Border)view.FindName("SidebarPanel");
                    DrawerCheck(view.IsCompactNavigation && view.IsNavigationOpen, "compact drawer open");
                    DrawerCheck(sidebar.Visibility == Visibility.Visible && sidebar.IsEnabled, "drawer interactive");
                    DrawerCheck(((Border)view.FindName("NavigationScrim")).Visibility == Visibility.Visible &&
                        !((Grid)view.FindName("ContentHost")).IsEnabled, "background dimmed and cannot receive keyboard input");
                    DrawerCheck(KeyboardNavigation.GetTabNavigation(sidebar) == KeyboardNavigationMode.Cycle &&
                        KeyboardNavigation.GetControlTabNavigation(sidebar) == KeyboardNavigationMode.Cycle, "Tab/Shift+Tab remain in drawer");
                    DrawerCheck(Math.Abs(dashboard.ActualWidth - 800) < 1, "opening overlays, never shifts content");
                    var grids = Visuals<DataGrid>(dashboard).ToArray();
                    DrawerCheck(grids.Select((g, i) => g.Columns.Select((c, j) => Math.Abs(c.ActualWidth - widths[i][j]) < 1).All(x => x)).All(x => x),
                        "opening drawer does not resize DataGrid columns");
                    foreach (var name in new[] { "OpenNavigation", "CloseNavigation" })
                    {
                        var button = (Button)view.FindName(name);
                        DrawerCheck(button.Focusable && button.IsTabStop && !string.IsNullOrWhiteSpace(new ButtonAutomationPeer(button).GetName()),
                            "accessible native keyboard button " + name);
                    }
                    return true;
                });
                await CloseDrawerAndWait(ui, view, () =>
                {
                    if (closeMode == "button") ClickDrawerButton(view, "CloseNavigation");
                    else if (closeMode == "scrim") ((Border)view.FindName("NavigationScrim")).RaiseEvent(
                        new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                    else ((Button)view.FindName("CloseNavigation")).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, hwnd, 0, Key.Escape)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                });
                await SetLayout(ui, host, 800, 600);
                await ui.Call(() =>
                {
                    DrawerCheck(!view.IsNavigationOpen && ((Grid)view.FindName("ContentHost")).IsEnabled, "close restores content interaction");
                    DrawerCheck(ReferenceEquals(Keyboard.FocusedElement, beforeFocus), "hidden/inactive host never steals foreground focus");
                    return true;
                });
            }
            foreach (var section in new[] { "Dashboard", "Zimmer", "Benutzer", "Dienstleistungen", "Events", "AlleReservierungen" })
            {
                await ui.Call(() => { ClickDrawerButton(view, "OpenNavigation"); return true; });
                await CloseDrawerAndWait(ui, view, () => ClickDrawerButton(view, "Show" + section));
                await ui.Call(() =>
                {
                    DrawerCheck(commands.Calls.Last() == section && ((ContentControl)view.FindName("ActiveItem")).Content is TextBlock { Text: var text } && text == section,
                        "existing Caliburn navigation executed before drawer closes: " + section);
                    return true;
                });
            }
            await ui.Call(() => { ClickDrawerButton(view, "OpenNavigation"); ClickDrawerButton(view, "CloseNavigation"); return true; });
            await SetLayout(ui, host, 1680, 900);
            await ui.Call(() =>
            {
                var sidebar = (Border)view.FindName("SidebarPanel");
                DrawerCheck(!view.IsCompactNavigation && sidebar.Visibility == Visibility.Visible && sidebar.IsEnabled,
                    "wide resize during closing animation restores permanent navigation");
                DrawerCheck(!((TranslateTransform)sidebar.RenderTransform).HasAnimatedProperties &&
                    ((TranslateTransform)sidebar.RenderTransform).X == 0, "stale animation removed on mode change");
                DrawerCheck(KeyboardNavigation.GetTabNavigation(sidebar) == KeyboardNavigationMode.Continue,
                    "wide navigation is not a focus trap");
                ClickDrawerButton(view, "Beenden");
                DrawerCheck(commands.Calls.Last() == "Beenden", "existing Beenden convention preserved (fake action)");
                return true;
            });
            foreach (var (width, compact) in new[] { (1430d, false), (1400d, true), (1430d, true), (1470d, false) })
            {
                await SetLayout(ui, host, width, 600);
                await ui.Call(() => { DrawerCheck(view.IsCompactNavigation == compact, "navigation breakpoint hysteresis"); return true; });
            }
            await SetLayout(ui, host, 800, 320);
            await ui.Call(() =>
            {
                ClickDrawerButton(view, "OpenNavigation");
                var sidebar = (Border)view.FindName("SidebarPanel");
                var navigationScroll = Visuals<ScrollViewer>(sidebar).Single();
                navigationScroll.ScrollToBottom();
                DrawerCheck(navigationScroll.ScrollableHeight > 0, "short window navigation scrolls");
                foreach (var name in new[] { "Abmelden", "Beenden" })
                {
                    var button = (Button)view.FindName(name);
                    var point = button.TranslatePoint(new Point(), view);
                    DrawerCheck(point.Y >= 0 && point.Y + button.ActualHeight <= view.ActualHeight + 1, "session action remains reachable: " + name);
                }
                ClickDrawerButton(view, "Abmelden");
                return true;
            });
            await SetLayout(ui, host, 800, 320);
            await ui.Call(() =>
            {
                DrawerCheck(commands.Calls.Last() == "Abmelden" && !view.IsLoaded && !view.IsNavigationOpen, "logout unload cancels drawer state");
                host.Child = view;
                return true;
            });
            await SetLayout(ui, host, 800, 600);
            await ui.Call(() =>
            {
                DrawerCheck(!view.IsNavigationOpen && ((Border)view.FindName("SidebarPanel")).Visibility == Visibility.Collapsed &&
                    ((Grid)view.FindName("ContentHost")).IsEnabled, "reload has no stale overlay or disabled content");
                return true;
            });
        }
        finally { await ui.Call(() => { host.Child = null; hwnd.RootVisual = null; hwnd.Dispose(); return true; }); }
        Console.WriteLine($"PASS: {adminDrawerAssertions} Admin drawer assertions; compiled View, real Caliburn binding/fake actions, no database. Foreground focus remains a manual check.");
    }
}
