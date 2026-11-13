using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Nest.Views.Behaviors;

// An empty virtualizing items-host reports zero horizontal extent, irrespective of headers.
// Supply only its missing measured width; never add rows or replace the native template.
public static class EmptyDataGridExtent
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(EmptyDataGridExtent), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(EmptyDataGridExtent));
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DataGrid grid) return;
        if ((bool)args.NewValue)
        {
            grid.Loaded += Loaded;
            grid.Unloaded += Unloaded;
            if (grid.IsLoaded) Loaded(grid, new RoutedEventArgs());
        }
        else
        {
            grid.Loaded -= Loaded;
            grid.Unloaded -= Unloaded;
            Unloaded(grid, new RoutedEventArgs());
        }
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        var grid = (DataGrid)sender;
        if (grid.GetValue(StateProperty) == null) grid.SetValue(StateProperty, new State(grid));
    }
    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        var grid = (DataGrid)sender;
        (grid.GetValue(StateProperty) as State)?.Dispose();
        grid.ClearValue(StateProperty);
    }

    private sealed class State : IDisposable
    {
        private readonly DataGrid grid;
        private DataGridRowsPresenter? rows;
        private double originalMinimum;
        private bool applied;
        public State(DataGrid grid)
        {
            this.grid = grid;
            grid.LayoutUpdated += Update;
            Update(null, EventArgs.Empty);
        }
        private void Update(object? sender, EventArgs args)
        {
            if (grid.HasItems) { Restore(); return; }
            rows ??= FindRows(grid);
            if (rows == null) return;
            if (!applied) { originalMinimum = rows.MinWidth; applied = true; }
            var width = Math.Max(originalMinimum, grid.Columns.Where(c => c.Visibility == Visibility.Visible).Sum(c => c.ActualWidth));
            // One corrective measure at most per changed column sum, not an endless layout loop.
            if (Math.Abs(rows.MinWidth - width) > 0.1) rows.SetCurrentValue(FrameworkElement.MinWidthProperty, width);
        }
        private void Restore()
        {
            if (applied && rows != null) rows.SetCurrentValue(FrameworkElement.MinWidthProperty, originalMinimum);
            applied = false;
        }
        public void Dispose() { grid.LayoutUpdated -= Update; Restore(); }
    }
    private static DataGridRowsPresenter? FindRows(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is DataGridRowsPresenter rows) return rows;
            if (FindRows(child) is { } nested) return nested;
        }
        return null;
    }
}
