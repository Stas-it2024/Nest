using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Nest.Views.CustomerPortal
{
    /// <summary>
    /// Interaktionslogik für CustomerPortalView.xaml.
    /// </summary>
    public partial class CustomerPortalView : UserControl
    {
        private bool contactMeasured, peopleMeasured, codeMeasured, pdfMeasured;
        private bool contactCompact, peopleCompact, codeCompact, pdfCompact;
        public CustomerPortalView()
        {
            // Im Code-Behind wird nur die Oberfläche initialisiert; die Programmlogik bleibt im ViewModel.
            InitializeComponent();
            // Reposition existing controls only, preserving bindings and unconverted input.
            while (ContactFields.RowDefinitions.Count < 5)
                ContactFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var grid in new[] { PeopleFields, ReservationCodeFields, PdfFields })
                for (var i = 0; i < 2; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ContactFields.SizeChanged += (_, _) => AdaptContacts();
            ContactFields.Loaded += (_, _) => AdaptContacts();
            PeopleFields.SizeChanged += (_, _) => AdaptPeople();
            PeopleFields.Loaded += (_, _) => AdaptPeople();
            ReservationCodeFields.SizeChanged += (_, _) => AdaptCode();
            ReservationCodeFields.Loaded += (_, _) => AdaptCode();
            PdfFields.SizeChanged += (_, _) => AdaptPdf();
            PdfFields.Loaded += (_, _) => AdaptPdf();
            SizeChanged += (_, _) =>
            {
                AdaptContacts(); AdaptPeople(); AdaptCode(); AdaptPdf();
            };
        }

        private static bool Changed(Grid grid, double threshold, ref bool measured, ref bool compact)
        {
            if (!grid.IsLoaded || grid.ActualWidth <= 0) return false;
            // Fixed columns can make ActualWidth larger than the allocated slot.
            // Use the parent's constraint, otherwise the old 200+200 grid can
            // remain 400 DIP wide even when only 260 DIP is available.
            var slot = LayoutInformation.GetLayoutSlot(grid);
            var available = Math.Min(grid.ActualWidth, Math.Max(0, slot.Width - grid.Margin.Left - grid.Margin.Right));
            var next = !measured ? available < threshold
                : compact ? available < threshold + 20 : available < threshold - 20;
            if (measured && next == compact) return false;
            compact = next; measured = true;
            return true;
        }

        private void AdaptContacts()
        {
            if (!Changed(ContactFields, 600, ref contactMeasured, ref contactCompact)) return;
            for (var col = 0; col < 3; col++)
                ContactFields.ColumnDefinitions[col].Width = contactCompact && col > 0 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            for (var i = 0; i < ContactFields.Children.Count; i++)
            {
                var child = ContactFields.Children[i];
                Grid.SetRow(child, contactCompact ? i : i / 3);
                Grid.SetColumn(child, contactCompact ? 0 : i % 3);
            }
        }

        private void AdaptPeople()
        {
            if (!Changed(PeopleFields, 420, ref peopleMeasured, ref peopleCompact)) return;
            PeopleFields.ColumnDefinitions[0].Width = peopleCompact ? new GridLength(1, GridUnitType.Star) : new GridLength(200);
            PeopleFields.ColumnDefinitions[1].Width = new GridLength(peopleCompact ? 0 : 200);
            Grid.SetRow(PeopleFields.Children[1], peopleCompact ? 1 : 0);
            Grid.SetColumn(PeopleFields.Children[1], peopleCompact ? 0 : 1);
        }

        private void AdaptCode()
        {
            if (!Changed(ReservationCodeFields, 560, ref codeMeasured, ref codeCompact)) return;
            ReservationCodeFields.ColumnDefinitions[1].Width = new GridLength(codeCompact ? 0 : 210);
            Grid.SetRow(LoadReservationButton, codeCompact ? 1 : 0);
            Grid.SetColumn(LoadReservationButton, codeCompact ? 0 : 1);
            LoadReservationButton.Margin = codeCompact ? new Thickness(0, 10, 0, 0) : new Thickness(12, 26, 0, 0);
        }

        private void AdaptPdf()
        {
            if (!Changed(PdfFields, 560, ref pdfMeasured, ref pdfCompact)) return;
            PdfFields.ColumnDefinitions[1].Width = new GridLength(pdfCompact ? 0 : 210);
            Grid.SetRow(OpenPdfButton, pdfCompact ? 1 : 0);
            Grid.SetColumn(OpenPdfButton, pdfCompact ? 0 : 1);
            PdfPathText.Margin = pdfCompact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 16, 0);
        }
    }
}
