using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nest.Views.Behaviors;

/// <summary>Presentation only: keeps one form, grid and DataContext alive across layout changes.</summary>
public static class CrudPaneLayout
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(CrudPaneLayout), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(CrudPaneLayout));
    public static bool GetIsEnabled(DependencyObject owner) => (bool)owner.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject owner, bool value) => owner.SetValue(IsEnabledProperty, value);

    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        if (owner is not UserControl view) return;
        (view.GetValue(StateProperty) as State)?.Dispose();
        view.SetValue(StateProperty, (bool)e.NewValue ? new State(view) : null);
    }

    private sealed class State : IDisposable
    {
        private const double Breakpoint = 960, Hysteresis = 24;
        private readonly UserControl view;
        private Grid workspace = null!;
        private ScrollViewer form = null!;
        private DataGrid table = null!;
        private FrameworkElement choices = null!;
        private RadioButton listChoice = null!, formChoice = null!;
        private Button create = null!, edit = null!;
        private bool attached, measured, compact, showForm, updating;
        private double formOffset;
        private UIElement? formFocus;
        private DispatcherOperation? pending;
        private long version;

        public State(UserControl view)
        {
            this.view = view;
            view.Loaded += Loaded;
            view.Unloaded += Unloaded;
            if (view.IsLoaded) Loaded(view, new RoutedEventArgs());
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            if (attached) return;
            workspace = (Grid)view.FindName("CrudWorkspace");
            form = (ScrollViewer)view.FindName("CrudForm");
            table = (DataGrid)view.FindName("CrudGrid");
            choices = (FrameworkElement)view.FindName("CrudPaneChoices");
            listChoice = (RadioButton)view.FindName("CrudListChoice");
            formChoice = (RadioButton)view.FindName("CrudFormChoice");
            create = (Button)view.FindName("OpenNewForm");
            edit = (Button)view.FindName("OpenEditForm");
            attached = true;
            view.SizeChanged += Resized;
            view.PreviewGotKeyboardFocus += FocusChanged;
            listChoice.Checked += ChooseList;
            formChoice.Checked += ChooseForm;
            create.Click += ChooseForm;
            edit.Click += ChooseForm;
            table.SelectionChanged += SelectionChanged;
            edit.IsEnabled = table.SelectedItem != null;
            Adapt();
        }

        private void Unloaded(object sender, RoutedEventArgs e)
        {
            version++;
            pending?.Abort(); pending = null;
            if (!attached) return;
            view.SizeChanged -= Resized;
            view.PreviewGotKeyboardFocus -= FocusChanged;
            listChoice.Checked -= ChooseList;
            formChoice.Checked -= ChooseForm;
            create.Click -= ChooseForm;
            edit.Click -= ChooseForm;
            table.SelectionChanged -= SelectionChanged;
            attached = false;
        }

        private void SelectionChanged(object sender, SelectionChangedEventArgs e) => edit.IsEnabled = table.SelectedItem != null;
        private void Resized(object sender, SizeChangedEventArgs e) => Adapt();
        private void FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is UIElement element && Within(element, form)) formFocus = element;
        }
        private void ChooseList(object sender, RoutedEventArgs e) => Choose(false);
        private void ChooseForm(object sender, RoutedEventArgs e) => Choose(true);
        private void Choose(bool value)
        {
            if (updating) return;
            Capture();
            showForm = value;
            Apply(requestFocus: true);
        }

        private void Adapt()
        {
            if (!attached || !view.IsLoaded || workspace.ActualWidth <= 0) return;
            var next = !measured ? workspace.ActualWidth < Breakpoint
                : compact ? workspace.ActualWidth < Breakpoint + Hysteresis : workspace.ActualWidth < Breakpoint - Hysteresis;
            if (measured && compact == next) return;
            Capture();
            if (next && !compact)
            {
                if (Within(Keyboard.FocusedElement as DependencyObject, form)) showForm = true;
                else if (Within(Keyboard.FocusedElement as DependencyObject, table)) showForm = false;
            }
            measured = true;
            compact = next;
            // Only a disappearing switcher needs focus repair on resize; never blur a field.
            Apply(requestFocus: !compact && Within(Keyboard.FocusedElement as DependencyObject, choices));
        }

        private void Capture()
        {
            if (form.IsVisible) formOffset = form.VerticalOffset;
        }

        private void Apply(bool requestFocus)
        {
            updating = true;
            try
            {
                workspace.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(300);
                workspace.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(table, compact ? 0 : 1);
                form.Visibility = !compact || showForm ? Visibility.Visible : Visibility.Collapsed;
                table.Visibility = !compact || !showForm ? Visibility.Visible : Visibility.Collapsed;
                choices.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
                listChoice.IsChecked = !showForm;
                formChoice.IsChecked = showForm;
                version++;
                var current = version;
                pending?.Abort();
                pending = view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    pending = null;
                    if (!attached || !view.IsLoaded || current != version) return;
                    if (form.IsVisible) form.ScrollToVerticalOffset(formOffset);
                    // Explicit switching (or a disappearing switcher) restores focus only
                    // while this window owns foreground input. No VM action is invoked.
                    if (!requestFocus || Window.GetWindow(view) is not { IsActive: true }) return;
                    if (showForm)
                    {
                        if (formFocus is { IsVisible: true, IsEnabled: true }) formFocus.Focus();
                        else form.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                    }
                    else table.Focus();
                }));
            }
            finally { updating = false; }
        }

        private static bool Within(DependencyObject? item, DependencyObject ancestor)
        {
            while (item != null)
            {
                if (ReferenceEquals(item, ancestor)) return true;
                item = item is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item);
            }
            return false;
        }
        public void Dispose()
        {
            Unloaded(view, new RoutedEventArgs());
            view.Loaded -= Loaded;
            view.Unloaded -= Unloaded;
        }
    }
}
