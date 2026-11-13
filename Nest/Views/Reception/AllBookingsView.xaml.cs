using System.Windows.Controls;
using Nest.Views.Behaviors;
using Nest.ViewModels.Admin;
using Nest.ViewModels.Reception;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Nest.Views.Reception
{
    public partial class AllBookingsView : UserControl
    {
        // Weakly scoped to the current authenticated navigation owner, not the whole process.
        // ShowAdmin/ShowReception create a new owner after each login. No auth code needs changing.
        private static readonly ConditionalWeakTable<object, DataGridColumnWidthSession> Sessions = new();
        private readonly DataGridColumnWidthSession? explicitWidths;
        private bool tracked;

        public AllBookingsView()
        {
            InitializeComponent();
            Loaded += TrackWidths;
        }

        public AllBookingsView(DataGridColumnWidthSession widths) : this()
        {
            explicitWidths = widths;
        }

        private void TrackWidths(object sender, RoutedEventArgs args)
        {
            if (tracked) return;
            object? owner = (DataContext as AllBookingsViewModel)?.Parent;
            if (owner is not AdminViewModel and not ReceptionViewModel) owner = null;
            for (var parent = VisualTreeHelper.GetParent(this); owner == null && parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is FrameworkElement { DataContext: AdminViewModel or ReceptionViewModel } element)
                    owner = element.DataContext;
            var widths = explicitWidths ?? (owner == null ? new DataGridColumnWidthSession()
                : Sessions.GetValue(owner, _ => new DataGridColumnWidthSession()));
            widths.Track(BookingGrid, "AllBookings.Reservations");
            widths.Track(TicketGrid, "AllBookings.Tickets");
            tracked = true;
        }

    }
}
