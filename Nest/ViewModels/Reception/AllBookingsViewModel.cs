using Caliburn.Micro;
using MySql.Data.MySqlClient;
using Nest.Data;
using Nest.Models;
using Nest.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using Nest.Synchronization;

namespace Nest.ViewModels.Reception
{
    /// <summary>
    /// Gemeinsame Übersicht für Zimmerreservierungen und Event-Tickets.
    /// </summary>
    public class AllBookingsViewModel : BookingPollingScreen
    {
        private readonly bool loadTickets;
        private IReadOnlyList<BookingSnapshot> cachedBookings = Array.Empty<BookingSnapshot>();
        private List<ReservationOverview> allReservations = new();
        private ReservationOverview? selectedReservation;
        private long ticketSession;
        private Task<List<TicketOrderItem>>? ticketRead;
        private readonly bool _showPageHeader;
        private string _searchText = "";
        private string _selectedStatus = "Alle";
        private ObservableCollection<ReservationOverview> _reservations = new();
        private ObservableCollection<TicketOrderItem> _ticketOrders = new();

        public AllBookingsViewModel(bool showPageHeader = false) : this(showPageHeader, () => PollingCoordinator.Shared, true) { }
        internal AllBookingsViewModel(bool showPageHeader, Func<PollingCoordinator> polling, bool loadTickets = false) : base(polling)
        {
            _showPageHeader = showPageHeader;
            this.loadTickets = loadTickets;
        }
        public ReservationOverview? SelectedReservation
        {
            get => selectedReservation;
            set { selectedReservation = value; NotifyOfPropertyChange(() => SelectedReservation); }
        }
        protected override async Task OnActivatedAsync(CancellationToken cancellationToken)
        {
            await base.OnActivatedAsync(cancellationToken);
            if (loadTickets) await LoadTicketOrders();
        }
        protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
        {
            ticketSession++;
            await base.OnDeactivateAsync(close, cancellationToken);
        }
        protected override void ApplyPollingSnapshot(PollingSnapshot snapshot)
        {
            if (cachedBookings.SequenceEqual(snapshot.Bookings)) return;
            cachedBookings = snapshot.Bookings;
            allReservations = snapshot.Bookings.Select(b => new ReservationOverview
            {
                ReservationCode = b.Code, Guest = b.Guest, Room = b.Room, Type = b.Type,
                CheckInDate = b.Arrival, CheckOutDate = b.Departure, Nights = b.Nights,
                Adults = b.Adults, Children = b.Children, TotalAmount = b.Total,
                AmountPaid = b.Paid, PaymentMethod = b.PaymentMethod, Status = b.Status
            }).ToList();
            FilterReservations();
        }

        public bool ShowPageHeader => _showPageHeader;

        public Thickness PageMargin => _showPageHeader ? new Thickness(0) : new Thickness(10);

        public Thickness ToolbarMargin => _showPageHeader
            ? new Thickness(10)
            : new Thickness(0, 0, 0, 10);

        public ObservableCollection<ReservationOverview> Reservations
        {
            get => _reservations;
            set
            {
                _reservations = value;
                NotifyOfPropertyChange(() => Reservations);
            }
        }

        public ObservableCollection<TicketOrderItem> TicketOrders
        {
            get => _ticketOrders;
            set
            {
                _ticketOrders = value;
                NotifyOfPropertyChange(() => TicketOrders);
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                NotifyOfPropertyChange(() => SearchText);
                FilterReservations();
            }
        }

        public string SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                _selectedStatus = value;
                NotifyOfPropertyChange(() => SelectedStatus);
                FilterReservations();
            }
        }

        public ObservableCollection<string> StatusOptions { get; } = new()
        {
            "Alle", "Reserviert", "Eingecheckt", "Ausgecheckt", "Storniert"
        };

        public void LoadAllReservations()
        {
            RequestPollingRefresh();
        }

        public async Task LoadTicketOrders()
        {
            var currentSession = ++ticketSession;
            Task<List<TicketOrderItem>>? read = null;
            try
            {
                read = ticketRead ??= Task.Run(ReadTicketOrders);
                var list = await read;
                if (ReferenceEquals(ticketRead, read)) ticketRead = null;
                if (PollingActive && currentSession == ticketSession)
                {
                    TicketOrders = new ObservableCollection<TicketOrderItem>(list);
                    TicketStatus = "";
                    NotifyOfPropertyChange(() => TicketStatus);
                }
            }
            catch (Exception)
            {
                if (ReferenceEquals(ticketRead, read)) ticketRead = null;
                // Optional tickets are not part of the shared booking snapshot.
                // Keep the previous list and report nonmodally, never from a worker thread.
                if (PollingActive && currentSession == ticketSession)
                { TicketStatus = "Tickets konnten nicht geladen werden. Bitte später erneut versuchen."; NotifyOfPropertyChange(() => TicketStatus); }
            }
        }
        public string TicketStatus { get; private set; } = "";
        private static List<TicketOrderItem> ReadTicketOrders()
        {
                var list = new List<TicketOrderItem>();
                using var conn = DB.GetConnection();
                DB.OpenConnection(conn);

                const string sql = @"
                    SELECT ticket_nummer, gast_name, gast_email, gast_telefon,
                           anzahl, einzelpreis, gesamtpreis, bestellt_am
                    FROM ticketbestellungen
                    ORDER BY bestellt_am DESC;";

                using var cmd = new MySqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new TicketOrderItem
                    {
                        TicketCode = reader.GetString("ticket_nummer"),
                        GuestName = reader.GetString("gast_name"),
                        GuestEmail = reader.IsDBNull(reader.GetOrdinal("gast_email")) ? "" : reader.GetString("gast_email"),
                        GuestPhone = reader.IsDBNull(reader.GetOrdinal("gast_telefon")) ? "" : reader.GetString("gast_telefon"),
                        Quantity = reader.GetInt32("anzahl"),
                        UnitPrice = reader.GetDecimal("einzelpreis"),
                        TotalPrice = reader.GetDecimal("gesamtpreis"),
                        DateOrdered = reader.GetDateTime("bestellt_am")
                    });
                }

                return list;
        }

        public void FilterReservations()
        {
                var code = SelectedReservation?.ReservationCode;
                var allData = allReservations;

                if (!string.IsNullOrEmpty(SelectedStatus) && SelectedStatus != "Alle")
                {
                    allData = allData.FindAll(r => string.Equals(r.Status, SelectedStatus, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    allData = allData.FindAll(r =>
                        (r.Guest?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (r.Room?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (r.ReservationCode?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
                }

                Reconcile(Reservations, allData, r => r.ReservationCode, (a, b) =>
                    a.ReservationCode == b.ReservationCode && a.Guest == b.Guest && a.Room == b.Room && a.Type == b.Type &&
                    a.CheckInDate == b.CheckInDate && a.CheckOutDate == b.CheckOutDate && a.Nights == b.Nights &&
                    a.Adults == b.Adults && a.Children == b.Children && a.TotalAmount == b.TotalAmount &&
                    a.AmountPaid == b.AmountPaid && a.PaymentMethod == b.PaymentMethod && a.Status == b.Status);
                var selected = Reservations.FirstOrDefault(r => r.ReservationCode == code);
                if (!ReferenceEquals(SelectedReservation, selected)) SelectedReservation = selected;
        }

        public void ClearSearch()
        {
            SearchText = "";
            SelectedStatus = "Alle";
            LoadAllReservations();
        }

        public new void Refresh()
        {
            LoadAllReservations();
        }

        public class TicketOrderItem
        {
            public string TicketCode { get; set; } = string.Empty;
            public string GuestName { get; set; } = string.Empty;
            public string GuestEmail { get; set; } = string.Empty;
            public string GuestPhone { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal TotalPrice { get; set; }
            public DateTime DateOrdered { get; set; }
        }
    }
}
