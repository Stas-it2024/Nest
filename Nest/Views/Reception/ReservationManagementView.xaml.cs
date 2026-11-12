using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nest.Views.Reception
{
    public partial class ReservationManagementView : UserControl
    {
        // Presentation state only. Never navigate via ActiveItem or activate/deactivate the VM.
        private const double WideWidth = 1180, WideHeight = 480, NarrowWidth = 640, Hysteresis = 20;
        private bool ready, measured, wide, narrow, showForm, updating;
        private Grid[] pairs = Array.Empty<Grid>();
        private UIElement? lastFormFocus, lastListFocus;
        private double formOffset, listVerticalOffset, listHorizontalOffset;
        private DispatcherOperation? pendingPresentation;
        private long focusVersion;

        public bool IsWideLayout => wide;
        public bool IsSingleColumnForm => narrow;

        public ReservationManagementView()
        {
            InitializeComponent();
            pairs = new[] { NamePair, ContactPair, IdentityPair, DatesPair, PricePair, PeoplePair, PaymentPair };
            foreach (var pair in pairs)
            {
                pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                pair.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            ready = true;
            SizeChanged += (_, _) => UpdatePresentation();
            StatusArea.SizeChanged += (_, _) => UpdatePresentation();
            Loaded += (_, _) => UpdatePresentation();
            Unloaded += (_, _) =>
            {
                focusVersion++;
                pendingPresentation?.Abort();
                pendingPresentation = null;
            };
            PreviewGotKeyboardFocus += (_, e) =>
            {
                focusVersion++;
                if (e.NewFocus is not UIElement element) return;
                if (Within(element, GuestFormPanel)) lastFormFocus = element;
                // A virtualized cell may later represent another booking. Remember
                // the stable grid instead, so focus restoration cannot select that row.
                else if (Within(element, ListPanel))
                    lastListFocus = Within(element, ReservationGrid) ? ReservationGrid : element;
            };
        }

        private void UpdatePresentation()
        {
            if (!ready || updating || ActualWidth <= 0 || ActualHeight <= 0) return;
            // Do not subtract the switcher's height: that would feed its own visibility
            // back into the decision and cause oscillation near the height boundary.
            var height = Math.Max(0, ActualHeight - StatusArea.ActualHeight -
                StatusArea.Margin.Top - StatusArea.Margin.Bottom);
            var nextWide = !measured
                ? ActualWidth >= WideWidth && height >= WideHeight
                : wide
                    ? ActualWidth >= WideWidth - Hysteresis && height >= WideHeight - Hysteresis
                    : ActualWidth >= WideWidth + Hysteresis && height >= WideHeight + Hysteresis;
            var nextNarrow = measured && narrow ? ActualWidth < NarrowWidth + Hysteresis : ActualWidth < NarrowWidth;
            if (measured && nextWide == wide && nextNarrow == narrow) return;

            CaptureScroll();
            var focus = Keyboard.FocusedElement as DependencyObject;
            if (!nextWide && wide)
            {
                // Keep the currently edited area visible; never blur an invalid numeric
                // TextBox merely because the window became narrower.
                if (Within(focus, GuestFormPanel)) showForm = true;
                else if (Within(focus, ListPanel)) showForm = false;
            }
            var switcherHadFocus = Within(focus, PaneSwitcher);
            wide = nextWide;
            narrow = nextNarrow;
            measured = true;
            ApplyPresentation(switcherHadFocus);
        }

        private void PaneChoice_Checked(object sender, RoutedEventArgs e)
        {
            if (!ready || updating) return;
            CaptureScroll();
            showForm = ReferenceEquals(sender, FormPaneChoice);
            ApplyPresentation(requestFocus: true);
        }

        private void ApplyPresentation(bool requestFocus)
        {
            updating = true;
            try
            {
                FormColumn.Width = wide ? new GridLength(440) : new GridLength(1, GridUnitType.Star);
                ListColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetColumn(ListPanel, wide ? 1 : 0);
                GuestFormPanel.Visibility = wide || showForm ? Visibility.Visible : Visibility.Collapsed;
                ListPanel.Visibility = wide || !showForm ? Visibility.Visible : Visibility.Collapsed;
                PaneSwitcher.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
                FormPaneChoice.IsChecked = showForm;
                ListPaneChoice.IsChecked = !showForm;
                foreach (var pair in pairs)
                {
                    pair.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 10);
                    pair.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                    var second = (FrameworkElement)pair.Children[1];
                    Grid.SetColumn(second, narrow ? 0 : 2);
                    Grid.SetRow(second, narrow ? 1 : 0);
                    second.Margin = narrow ? new Thickness(0, 6, 0, 0) : new Thickness(0);
                }
            }
            finally { updating = false; }

            pendingPresentation?.Abort();
            if (Dispatcher.HasShutdownStarted) return;
            var version = focusVersion;
            pendingPresentation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                pendingPresentation = null;
                if (!IsLoaded) return;
                if (GuestFormPanel.Visibility == Visibility.Visible) FormScroller.ScrollToVerticalOffset(formOffset);
                if (ListPanel.Visibility == Visibility.Visible && FindScroll(ReservationGrid) is { } scroll)
                {
                    scroll.ScrollToVerticalOffset(listVerticalOffset);
                    scroll.ScrollToHorizontalOffset(listHorizontalOffset);
                }
                if (!requestFocus || version != focusVersion) return;
                var window = Window.GetWindow(this);
                // A MessageBox disables its owner. Never steal focus from a modal
                // window, another application, or a later user action.
                if (window is not { IsActive: true, IsEnabled: true } ||
                    (!IsKeyboardFocusWithin && Keyboard.FocusedElement != null)) return;
                var target = showForm ? lastFormFocus : lastListFocus;
                target ??= showForm ? FirstInput(GuestFormPanel) : ReservationSearch;
                if (target is { IsVisible: true, IsEnabled: true, Focusable: true })
                {
                    target.Focus();
                    if (target is FrameworkElement element) element.BringIntoView();
                }
            }));
        }

        private void CaptureScroll()
        {
            if (GuestFormPanel.Visibility == Visibility.Visible) formOffset = FormScroller.VerticalOffset;
            if (ListPanel.Visibility == Visibility.Visible && FindScroll(ReservationGrid) is { } scroll)
            {
                listVerticalOffset = scroll.VerticalOffset;
                listHorizontalOffset = scroll.HorizontalOffset;
            }
        }

        private static bool Within(DependencyObject? element, DependencyObject ancestor)
        {
            while (element != null)
            {
                if (ReferenceEquals(element, ancestor)) return true;
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private static ScrollViewer? FindScroll(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer scroll) return scroll;
                if (FindScroll(child) is { } nested) return nested;
            }
            return null;
        }

        private static UIElement? FirstInput(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBox input) return input;
                if (FirstInput(child) is { } nested) return nested;
            }
            return null;
        }
    }
}
