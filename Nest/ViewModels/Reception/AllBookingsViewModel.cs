using Caliburn.Micro;
using MySql.Data.MySqlClient;
using Nest.Data;
using Nest.Models;
using Nest.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace Nest.ViewModels.Reception
{
    /// <summary>
    /// Gemeinsame Übersicht für Zimmerreservierungen und Event-Tickets.
    /// </summary>
    public class AllBookingsViewModel : Screen
    {
        private readonly ReservationRepository _reservationRepository;
        private readonly bool _showPageHeader;
        private string _searchText = "";
        private string _selectedStatus = "Alle";
        private ObservableCollection<ReservationOverview> _reservations;
        private ObservableCollection<TicketOrderItem> _ticketOrders;

        public AllBookingsViewModel(bool showPageHeader = false)
        {
            _showPageHeader = showPageHeader;
            _reservationRepository = new ReservationRepository();

            LoadAllReservations();
            LoadTicketOrders();
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
            try
            {
                Reservations = new ObservableCollection<ReservationOverview>(_reservationRepository.GetAll());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Laden der Reservierungen: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadTicketOrders()
        {
            try
            {
                var list = new List<TicketOrderItem>();
                using var conn = DB.GetConnection();
                conn.Open();

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

                TicketOrders = new ObservableCollection<TicketOrderItem>(list);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Laden der Tickets: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void FilterReservations()
        {
            try
            {
                var allData = _reservationRepository.GetAll();

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

                Reservations = new ObservableCollection<ReservationOverview>(allData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Filtern: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
