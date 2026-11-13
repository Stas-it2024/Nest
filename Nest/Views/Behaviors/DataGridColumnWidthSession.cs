using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Nest.Views.Behaviors;

// Presentation state only. The owner chooses its lifetime; no files, database or View references.
public sealed class DataGridColumnWidthSession
{
    // Initial automatic allocation is distinct from the user's technical resize minimum.
    public static readonly DependencyProperty InitialMinWidthProperty = DependencyProperty.RegisterAttached(
        "InitialMinWidth", typeof(double), typeof(DataGridColumnWidthSession), new PropertyMetadata(0d));
    public static double GetInitialMinWidth(DependencyObject target) => (double)target.GetValue(InitialMinWidthProperty);
    public static void SetInitialMinWidth(DependencyObject target, double value) => target.SetValue(InitialMinWidthProperty, value);
    private readonly Dictionary<string, Dictionary<string, DataGridLength>> widths = new(StringComparer.Ordinal);

    public void Track(DataGrid grid, string tableKey)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableKey);
        grid.Dispatcher.VerifyAccess();
        _ = new Binding(this, grid, tableKey);
    }

    private sealed class Binding
    {
        private readonly DataGridColumnWidthSession session;
        private readonly DataGrid grid;
        private readonly string tableKey;
        private bool active;
        private HashSet<DataGridColumn>? resizing;
        private Dictionary<DataGridColumn, double>? originalMinima;
        private readonly Dictionary<DataGridColumn, double> userMinima = new();

        public Binding(DataGridColumnWidthSession session, DataGrid grid, string tableKey)
        {
            this.session = session;
            this.grid = grid;
            this.tableKey = tableKey;
            foreach (var column in grid.Columns) userMinima[column] = column.MinWidth;
            grid.Loaded += Loaded;
            grid.Unloaded += Unloaded;
            if (grid.IsLoaded) Start();
        }

        private void Loaded(object sender, RoutedEventArgs args) => Start();
        private void Unloaded(object sender, RoutedEventArgs args)
        {
            if (!active) return;
            active = false;
            resizing = null;
            grid.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(DragStarted));
            grid.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(DragCompleted));
            grid.RemoveHandler(Control.MouseDoubleClickEvent, new MouseButtonEventHandler(DoubleClick));
        }

        private void Start()
        {
            if (active) return;
            foreach (var column in grid.Columns)
            {
                // Binding property paths are stable across localization and fresh View instances.
                var key = column.SortMemberPath;
                if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("A stable column key is required.");
                if (session.widths.TryGetValue(tableKey, out var saved) && saved.TryGetValue(key, out var width))
                {
                    column.MinWidth = userMinima[column];
                    column.Width = width;
                }
                else
                    column.MinWidth = Math.Max(userMinima[column], column.Width.IsAbsolute
                        ? column.Width.Value : GetInitialMinWidth(column));
            }
            active = true;
            grid.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(DragStarted), true);
            grid.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(DragCompleted), true);
            grid.AddHandler(Control.MouseDoubleClickEvent, new MouseButtonEventHandler(DoubleClick), true);
        }

        private DataGridColumn? ResizeColumn(object source)
        {
            var node = source as DependencyObject;
            while (node != null && node is not Thumb)
                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            if (node is not Thumb thumb || thumb.TemplatedParent is not DataGridColumnHeader header || header.Column == null)
                return null;
            if (thumb.Name == "PART_RightHeaderGripper") return header.Column;
            if (thumb.Name == "PART_LeftHeaderGripper")
                for (var index = header.Column.DisplayIndex - 1; index >= 0; index--)
                {
                    var previous = grid.ColumnFromDisplayIndex(index);
                    if (previous.Visibility == Visibility.Visible) return previous;
                }
            return null;
        }

        private void DragCompleted(object sender, DragCompletedEventArgs args)
        {
            if (args.Canceled && originalMinima != null)
                foreach (var (column, minimum) in originalMinima) column.MinWidth = minimum;
            if (!args.Canceled && resizing != null)
                foreach (var column in resizing) Save(column, column.Width);
            resizing = null;
            originalMinima = null;
        }

        private void DragStarted(object sender, DragStartedEventArgs args)
        {
            if (ResizeColumn(args.OriginalSource) is not { } target) return;
            resizing = new() { target };
            originalMinima = new();
            // WPF refuses to grow the last star/fixed column if all columns to its right
            // are already at their minima. Once the user takes control, freeze the
            // initial star allocation, allowing native resizing and horizontal overflow.
            var stars = new List<(DataGridColumn Column, double Width)>();
            foreach (var column in grid.Columns)
                if (column.Visibility == Visibility.Visible && column.Width.IsStar)
                    stars.Add((column, column.ActualWidth));
            foreach (var (column, width) in stars)
            {
                resizing.Add(column);
                column.Width = new DataGridLength(width);
            }
            foreach (var column in grid.Columns)
            {
                originalMinima[column] = column.MinWidth;
                column.MinWidth = userMinima[column];
            }
        }

        private void DoubleClick(object sender, MouseButtonEventArgs args)
        {
            if (ResizeColumn(args.OriginalSource) is { } column)
                if (column.Width.IsAuto) Save(column, DataGridLength.Auto);
        }

        private void Save(DataGridColumn column, DataGridLength width)
        {
            if (!active || !grid.IsLoaded || grid.Dispatcher.HasShutdownStarted) return;
            if (!session.widths.TryGetValue(tableKey, out var table))
                session.widths[tableKey] = table = new(StringComparer.Ordinal);
            table[column.SortMemberPath] = new DataGridLength(width.Value, width.UnitType);
        }
    }
}
