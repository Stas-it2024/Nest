using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Nest.Views.Admin
{
    public partial class AdminView : UserControl
    {
        private const double WideWidth = 1440, Hysteresis = 24;
        private readonly DependencyPropertyDescriptor contentDescriptor;
        private bool measured, compact, menuOpen;
        private long presentationVersion;
        private IInputElement? previousFocus;
        private DispatcherOperation? pendingFocus, pendingNavigation;

        public bool IsCompactNavigation => compact;
        public bool IsNavigationOpen => menuOpen;

        public AdminView()
        {
            InitializeComponent();
            contentDescriptor = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
            Loaded += (_, _) =>
            {
                contentDescriptor.RemoveValueChanged(ActiveItem, ContentChanged);
                contentDescriptor.AddValueChanged(ActiveItem, ContentChanged);
                AdaptNavigation();
            };
            SizeChanged += (_, _) => AdaptNavigation();
            Unloaded += (_, _) =>
            {
                contentDescriptor.RemoveValueChanged(ActiveItem, ContentChanged);
                CancelPending();
                menuOpen = false;
                previousFocus = null;
                SidebarShift.BeginAnimation(TranslateTransform.XProperty, null);
                NavigationScrim.Visibility = Visibility.Collapsed;
                ContentHost.IsEnabled = true;
                SidebarPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            };
            PreviewKeyDown += (_, e) =>
            {
                if (menuOpen && e.Key == Key.Escape) { e.Handled = true; CloseDrawer(true); }
            };
        }

        private void AdaptNavigation()
        {
            if (!IsLoaded || ActualWidth <= 0) return;
            var nextCompact = !measured ? ActualWidth < WideWidth
                : compact ? ActualWidth < WideWidth + Hysteresis : ActualWidth < WideWidth - Hysteresis;
            SidebarPanel.Width = Math.Min(240, ActualWidth);
            if (measured && nextCompact == compact) return;
            var focused = Keyboard.FocusedElement as DependencyObject;
            var sidebarHadFocus = Within(focused, SidebarPanel);
            var toggleHadFocus = Within(focused, OpenNavigation) || Within(focused, CloseNavigation);
            CancelPending();
            measured = true;
            compact = nextCompact;
            menuOpen = false;
            previousFocus = null;
            SidebarShift.BeginAnimation(TranslateTransform.XProperty, null);
            SidebarShift.X = 0;
            SidebarColumn.Width = new GridLength(compact ? 0 : 240);
            Grid.SetColumnSpan(SidebarPanel, compact ? 2 : 1);
            SidebarPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            SidebarPanel.IsEnabled = !compact;
            CloseNavigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            CompactToolbar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            NavigationScrim.Visibility = Visibility.Collapsed;
            ContentHost.IsEnabled = true;
            KeyboardNavigation.SetTabNavigation(SidebarPanel, compact ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
            KeyboardNavigation.SetControlTabNavigation(SidebarPanel, compact ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
            if (compact && sidebarHadFocus) ScheduleFocus(OpenNavigation);
            else if (!compact && toggleHadFocus) ScheduleFocus(ShowDashboard);
        }

        private void OpenNavigation_Click(object sender, RoutedEventArgs e)
        {
            if (!compact || menuOpen || !IsLoaded) return;
            CancelPending();
            previousFocus = Keyboard.FocusedElement;
            menuOpen = true;
            SidebarPanel.Visibility = Visibility.Visible;
            SidebarPanel.IsEnabled = true;
            ContentHost.IsEnabled = false;
            NavigationScrim.Visibility = Visibility.Visible;
            AnimateSidebar(true);
            ScheduleFocus(CloseNavigation);
        }

        private void CloseNavigation_Click(object sender, RoutedEventArgs e) => CloseDrawer(true);
        private void NavigationScrim_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            CloseDrawer(true);
        }

        private void CloseDrawer(bool restoreFocus)
        {
            if (!menuOpen) return;
            CancelPending();
            menuOpen = false;
            SidebarPanel.IsEnabled = false;
            ContentHost.IsEnabled = true;
            NavigationScrim.Visibility = Visibility.Collapsed;
            AnimateSidebar(false);
            var target = previousFocus as UIElement;
            previousFocus = null;
            if (restoreFocus) ScheduleFocus(target is { IsVisible: true, IsEnabled: true } && Within(target, ContentHost) ? target : OpenNavigation);
        }

        private void AnimateSidebar(bool opening)
        {
            var version = presentationVersion;
            var from = opening ? -SidebarPanel.Width : SidebarShift.X;
            var to = opening ? 0 : -SidebarPanel.Width;
            SidebarShift.BeginAnimation(TranslateTransform.XProperty, null);
            SidebarShift.X = to;
            if (!SystemParameters.ClientAreaAnimation)
            {
                if (!opening) SidebarPanel.Visibility = Visibility.Collapsed;
                return;
            }
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(160))
            { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            animation.Completed += (_, _) =>
            {
                if (version != presentationVersion || !IsLoaded) return;
                SidebarShift.BeginAnimation(TranslateTransform.XProperty, null);
                if (!opening && compact && !menuOpen) SidebarPanel.Visibility = Visibility.Collapsed;
            };
            SidebarShift.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void ContentChanged(object? sender, EventArgs e)
        {
            if (!compact || !menuOpen) return;
            var version = presentationVersion;
            var content = ActiveItem.Content;
            pendingNavigation?.Abort();
            // React AFTER existing Caliburn navigation has installed the new content.
            pendingNavigation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                pendingNavigation = null;
                if (IsLoaded && version == presentationVersion && ReferenceEquals(content, ActiveItem.Content)) CloseDrawer(true);
            }));
        }

        private void CancelPending()
        {
            presentationVersion++;
            pendingFocus?.Abort(); pendingFocus = null;
            pendingNavigation?.Abort(); pendingNavigation = null;
        }

        private void ScheduleFocus(UIElement target)
        {
            var version = presentationVersion;
            pendingFocus = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                pendingFocus = null;
                var window = Window.GetWindow(this);
                if (IsLoaded && version == presentationVersion && window is { IsActive: true } && target.IsVisible && target.IsEnabled)
                    target.Focus(); // Never take foreground focus from another app or a modal window.
            }));
        }

        private static bool Within(DependencyObject? element, DependencyObject parent)
        {
            while (element != null)
            {
                if (ReferenceEquals(element, parent)) return true;
                element = element is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }
    }
}
