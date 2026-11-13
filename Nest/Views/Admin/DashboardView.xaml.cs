using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nest.Views.Admin
{
    public partial class DashboardView : UserControl
    {
        private const double CardWidth = 180, WideCardsWidth = 1120, WideTablesWidth = 1800, Hysteresis = 24, MinimumTableHeight = 280;
        private bool measured, sideBySide;
        public bool AreTablesSideBySide => sideBySide;
        public int MetricColumnCount { get; private set; } = 1;

        public DashboardView()
        {
            InitializeComponent();
            Loaded += (_, _) => AdaptLayout();
            SizeChanged += (_, _) => AdaptLayout();
            MetricCards.SizeChanged += (_, _) => AdaptLayout();
            PageHeader.SizeChanged += (_, _) => AdaptLayout();
            PageScroller.PreviewMouseWheel += RoutePageWheel;
        }

        private void AdaptLayout()
        {
            if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
            // Reserve a possible scrollbar so its appearance cannot alternate the card count.
            // No minimum width is forced on the page or the window.
            var width = Math.Max(1, ActualWidth - SystemParameters.VerticalScrollBarWidth - 8);
            var maximumColumns = width >= WideCardsWidth ? 8 : Math.Clamp((int)(width / CardWidth), 1, 4);
            var rows = (int)Math.Ceiling(8d / maximumColumns);
            var columns = (int)Math.Ceiling(8d / rows); // Prefer balanced rows (4+4 rather than 6+2).
            MetricColumnCount = columns;
            var revenueWidth = columns == 8 ? Math.Max(240, width / 8 * 1.35) : width / columns;
            var countWidth = columns == 8 ? (width - revenueWidth) / 7 : width / columns;
            for (var index = 0; index < MetricCards.Children.Count; index++)
            {
                var card = (FrameworkElement)MetricCards.Children[index];
                var cardWidth = Math.Max(1, (index == 7 ? revenueWidth : countWidth) - card.Margin.Left - card.Margin.Right);
                if (double.IsNaN(card.Width) || Math.Abs(card.Width - cardWidth) > 0.1) card.Width = cardWidth;
            }

            var nextSideBySide = !measured ? width >= WideTablesWidth
                : sideBySide ? width >= WideTablesWidth - Hysteresis : width >= WideTablesWidth + Hysteresis;
            if (!measured || nextSideBySide != sideBySide)
            {
                sideBySide = nextSideBySide;
                measured = true;
                FirstTableColumn.Width = new GridLength(sideBySide ? 2 : 1, GridUnitType.Star);
                SecondTableColumn.Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                SecondTableRow.Height = sideBySide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                Grid.SetRow(RoomsPanel, sideBySide ? 0 : 1);
                Grid.SetColumn(RoomsPanel, sideBySide ? 1 : 0);
                ReservationsPanel.Margin = sideBySide ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 8);
            }

            // Finite table viewports preserve virtualization inside the page scroller.
            // At short heights the page scrolls instead of collapsing a table to zero.
            var freeHeight = Math.Max(0, ActualHeight - PageHeader.ActualHeight - MetricCards.ActualHeight - 16);
            var height = Math.Max(MinimumTableHeight, (freeHeight - (sideBySide ? 0 : 8)) / (sideBySide ? 1 : 2));
            var totalHeight = sideBySide ? height : height * 2 + 8;
            if (Math.Abs(TablesHost.Height - totalHeight) > 0.5) TablesHost.Height = totalHeight;
        }

        private void RoutePageWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0 || PageScroller.ScrollableHeight <= 0) return;
            DependencyObject? node = e.OriginalSource as DependencyObject;
            while (node != null && node is not ScrollViewer)
                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            if (node is not ScrollViewer inner || ReferenceEquals(inner, PageScroller)) return;
            // A table keeps its native wheel behavior while it can scroll in this direction.
            // At a boundary (or over a value-only horizontal scroller), let the PAGE continue.
            if (inner.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
                (e.Delta < 0 ? inner.VerticalOffset < inner.ScrollableHeight : inner.VerticalOffset > 0)) return;
            var lines = SystemParameters.WheelScrollLines;
            var steps = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine);
            for (var step = 0; step < steps; step++)
                if (lines < 0) { if (e.Delta < 0) PageScroller.PageDown(); else PageScroller.PageUp(); }
                else for (var line = 0; line < lines; line++)
                    if (e.Delta < 0) PageScroller.LineDown(); else PageScroller.LineUp();
            e.Handled = true;
        }
    }
}
