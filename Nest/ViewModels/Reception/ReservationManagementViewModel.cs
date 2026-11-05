using Caliburn.Micro;
using MySql.Data.MySqlClient;
using Nest.Data;
using Nest.Models;
using Nest.ViewModels;
using Nest.ViewModels.CustomerPortal;
using Nest.ViewModels.Shell;
using System;
using System.Collections.ObjectModel;
using System.Data;

namespace Nest.ViewModels.Reception
{
    public class ReservationManagementViewModel : Screen
    {
        private readonly ShellViewModel _shell;
        private readonly RoomRepository _roomRepository;
        private readonly ReservationRepository _reservationRepository;
        private readonly GastRepository _gastRepository;

        // ==================== KONSTRUKTOR ====================
        public ReservationManagementViewModel(ShellViewModel shell)
        {
            _shell = shell;

            _roomRepository = new RoomRepository();
            _reservationRepository = new ReservationRepository();
            _gastRepository = new GastRepository();

            // Commands
            // Standardwerte
            CheckInDate = DateTime.Today;
            CheckOutDate = DateTime.Today.AddDays(1);
            Adults = 1;
            Children = 0;
            PaymentType = "Bar";
            Deposit = 0;

            LoadRoomsFromDatabase();
            LoadReservationsFromDatabase();

            if (RoomList.Count > 0)
                SelectedRoom = RoomList[0];

            CalculatePrice();
        }

        // ==================== COMMANDS ====================
        // ==================== GÄSTEFORMULAR ====================
        private string _firstName = "";
        public string FirstName
        {
            get => _firstName;
            set { _firstName = value; NotifyOfPropertyChange(() => FirstName); }
        }

        private string _lastName = "";
        public string LastName
        {
            get => _lastName;
            set { _lastName = value; NotifyOfPropertyChange(() => LastName); }
        }

        private string _email = "";
        public string Email
        {
            get => _email;
            set { _email = value; NotifyOfPropertyChange(() => Email); }
        }

        private string _phone = "";
        public string Phone
        {
            get => _phone;
            set { _phone = value; NotifyOfPropertyChange(() => Phone); }
        }

        private string _idType = "Reisepass";
        public string IdType
        {
            get => _idType;
            set { _idType = value; NotifyOfPropertyChange(() => IdType); }
        }

        private string _idNumber = "";
        public string IdNumber
        {
            get => _idNumber;
            set { _idNumber = value; NotifyOfPropertyChange(() => IdNumber); }
        }

        private string _nationality = "";
        public string Nationality
        {
            get => _nationality;
            set { _nationality = value; NotifyOfPropertyChange(() => Nationality); }
        }

        // ==================== ZIMMER / AUFENTHALT ====================
        private Room? _selectedRoom;
        public Room? SelectedRoom
        {
            get => _selectedRoom;
            set
            {
                _selectedRoom = value;
                NotifyOfPropertyChange(() => SelectedRoom);
                CalculatePrice();
            }
        }

        private DateTime _checkInDate;
        public DateTime CheckInDate
        {
            get => _checkInDate;
            set
            {
                _checkInDate = value;
                NotifyOfPropertyChange(() => CheckInDate);
                CalculateNights();
                CalculatePrice();
            }
        }

        private DateTime _checkOutDate;
        public DateTime CheckOutDate
        {
            get => _checkOutDate;
            set
            {
                _checkOutDate = value;
                NotifyOfPropertyChange(() => CheckOutDate);
                CalculateNights();
                CalculatePrice();
            }
        }

        private int _nights;
        public int Nights
        {
            get => _nights;
            set { _nights = value; NotifyOfPropertyChange(() => Nights); CalculatePrice(); }
        }

        private decimal _price;
        public decimal Price
        {
            get => _price;
            set { _price = value; NotifyOfPropertyChange(() => Price); }
        }

        private int _adults;
        public int Adults
        {
            get => _adults;
            set { _adults = value; NotifyOfPropertyChange(() => Adults); }
        }

        private int _children;
        public int Children
        {
            get => _children;
            set { _children = value; NotifyOfPropertyChange(() => Children); }
        }

        private string _paymentType = "Bar";
        public string PaymentType
        {
            get => _paymentType;
            set { _paymentType = value; NotifyOfPropertyChange(() => PaymentType); }
        }

        private decimal _deposit;
        public decimal Deposit
        {
            get => _deposit;
            set { _deposit = value; NotifyOfPropertyChange(() => Deposit); }
        }

        private string _specialRequests = "";
        public string SpecialRequests
        {
            get => _specialRequests;
            set { _specialRequests = value; NotifyOfPropertyChange(() => SpecialRequests); }
        }

        // ==================== SUCHE ====================
        private string _searchText = "";
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

        private ReservationItem? _selectedReservation;
        public ReservationItem? SelectedReservation
        {
            get => _selectedReservation;
            set
            {
                _selectedReservation = value;
                NotifyOfPropertyChange(() => SelectedReservation);
            }
        }

        // ==================== LISTEN ====================
        public ObservableCollection<Room> RoomList { get; set; } = new();
        public ObservableCollection<string> IdTypeList { get; set; } = new()
        { "Reisepass", "Personalausweis", "Fuehrerschein", "Sonstiges" };
        public ObservableCollection<int> AdultOptions { get; set; } = new() { 0, 1, 2, 3, 4 };
        public ObservableCollection<int> ChildOptions { get; set; } = new() { 0, 1, 2, 3 };
        public ObservableCollection<string> PaymentOptions { get; set; } = new()
        { "Bar", "Kreditkarte", "Debitkarte", "Ueberweisung", "Online" };

        public ObservableCollection<ReservationItem> Reservations { get; set; } = new();
        // Aktuell angezeigte View (null = Standard-Reservierungsansicht)
        private object? _activeItem;
        public object? ActiveItem
        {
            get => _activeItem;
            set
            {
                _activeItem = value;
                NotifyOfPropertyChange(() => ActiveItem);
            }
        }

        // ==================== BERECHNUNGEN ====================
        private void CalculateNights()
        {
            Nights = CheckOutDate > CheckInDate ? (CheckOutDate - CheckInDate).Days : 0;
        }

        private void CalculatePrice()
        {
            if (SelectedRoom == null || Nights <= 0)
            {
                Price = 0;
                return;
            }
            Price = Nights * SelectedRoom.PricePerNight;
        }

        // ==================== DATEN LADEN ====================
        private bool LoadRoomsFromDatabase(bool showError = true)
        {
            try
            {
                var selectedRoomId = SelectedRoom?.Id;
                var rooms = new ObservableCollection<Room>(_roomRepository.GetAll());
                RoomList = rooms;
                NotifyOfPropertyChange(() => RoomList);
                SelectedRoom = rooms.FirstOrDefault(room => room.Id == selectedRoomId);
                return true;
            }
            catch (Exception)
            {
                if (showError)
                    System.Windows.MessageBox.Show("Die Zimmerliste konnte nicht geladen werden. Bitte versuchen Sie es erneut.",
                        "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        private bool LoadReservationsFromDatabase(bool showError = true)
        {
            try
            {
                var data = _reservationRepository.GetAll();

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    data = data.FindAll(r =>
                        (r.Guest?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (r.ReservationCode?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
                }

                var selectedCode = SelectedReservation?.ReservationCode;
                var reservations = new ObservableCollection<ReservationItem>();
                foreach (var r in data)
                {
                    reservations.Add(new ReservationItem
                    {
                        ReservationCode = r.ReservationCode,
                        GastName = r.Guest,
                        RoomNumber = r.Room,
                        RoomType = r.Type,
                        CheckOut = r.CheckOutDate,
                        Nights = r.Nights,
                        Amount = r.TotalAmount,
                        Status = r.Status
                    });
                }
                Reservations = reservations;
                NotifyOfPropertyChange(() => Reservations);
                SelectedReservation = reservations.FirstOrDefault(r => r.ReservationCode == selectedCode);
                return true;
            }
            catch (Exception)
            {
                if (showError)
                    System.Windows.MessageBox.Show("Die Reservierungsliste konnte nicht geladen werden. Bitte versuchen Sie es erneut.",
                        "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public void FilterReservations()
        {
            LoadReservationsFromDatabase();
        }

        public void ClearSearch()
        {
            SearchText = "";
        }

        // ==================== RESERVIERUNG SPEICHERN ====================
        public void SaveReservation()
        {
            if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
            {
                System.Windows.MessageBox.Show("Bitte Vorname und Nachname eingeben!", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            if (SelectedRoom == null)
            {
                System.Windows.MessageBox.Show("Bitte ein Zimmer auswählen!", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            if (CheckInDate.Date >= CheckOutDate.Date)
            {
                System.Windows.MessageBox.Show("Check-out muss nach Check-in liegen!", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                var gast = new Guest
                {
                    FirstName = FirstName, LastName = LastName, Email = Email, Phone = Phone,
                    IdType = IdType, IdNumber = IdNumber, Nationality = Nationality, DateAdded = DateTime.Now
                };
                var reservierung = new Reservation
                {
                    RoomId = SelectedRoom.Id,
                    CheckInDate = CheckInDate.Date,
                    CheckOutDate = CheckOutDate.Date,
                    Nights = (CheckOutDate.Date - CheckInDate.Date).Days,
                    Adults = Adults, Children = Children,
                    TotalAmount = Price, AmountPaid = Deposit,
                    PaymentMethod = PaymentType, Status = "Reserviert",
                    ReservationCode = "BNR-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
                    SpecialRequests = SpecialRequests, DateCreated = DateTime.Now
                };
                var validationError = ReservationRepository.GetBookingValidationError(reservierung);
                if (validationError != null || (long)Adults + Children > SelectedRoom.Capacity)
                {
                    System.Windows.MessageBox.Show(validationError ?? "Die Zimmerkapazität darf nicht überschritten werden.",
                        "Ungültige Angaben", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                if (!_reservationRepository.TryAddIfRoomAvailable(reservierung, gast))
                {
                    System.Windows.MessageBox.Show("Das Zimmer ist im ausgewählten Zeitraum nicht verfügbar.",
                        "Zimmer nicht verfügbar", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                System.Windows.MessageBox.Show(
                    $"Reservierung gespeichert!\nBuchungsnummer: {reservierung.ReservationCode}\nGesamtpreis: {Price:C}\nAnzahlung: {Deposit:C}",
                    "Erfolg", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                LoadReservationsFromDatabase();
                LoadRoomsFromDatabase();
                ResetForm();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ResetForm()
        {
            FirstName = "";
            LastName = "";
            Email = "";
            Phone = "";
            IdNumber = "";
            Nationality = "";
            SpecialRequests = "";
            Deposit = 0;
            CheckInDate = DateTime.Today;
            CheckOutDate = DateTime.Today.AddDays(1);
            if (RoomList.Count > 0)
                SelectedRoom = RoomList[0];
        }

        // ==================== CHECK-IN ====================
        public void CheckIn()
        {
            if (SelectedReservation == null)
            {
                System.Windows.MessageBox.Show("Bitte eine Reservierung auswählen.", "Hinweis",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (SelectedReservation.Status != "Reserviert")
            {
                System.Windows.MessageBox.Show("Nur Reservierungen mit Status 'Reserviert' können eingecheckt werden.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ExecuteStayTransition(SelectedReservation, "Check-in",
                "UPDATE reservierungen SET status='Eingecheckt' WHERE reservierungs_code=@code AND status='Reserviert'",
                @"UPDATE zimmer z
                  JOIN reservierungen r ON r.zimmer_id = z.id
                  SET z.status = 'Belegt'
                  WHERE r.reservierungs_code = @code
                    AND r.status = 'Eingecheckt';");
        }

        // ==================== CHECK-OUT ====================
        public void CheckOut()
        {
            if (SelectedReservation == null)
            {
                System.Windows.MessageBox.Show("Bitte eine Reservierung auswählen.", "Hinweis",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (SelectedReservation.Status != "Eingecheckt")
            {
                System.Windows.MessageBox.Show("Nur eingecheckte Reservierungen können ausgecheckt werden.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            ExecuteStayTransition(SelectedReservation, "Check-out",
                "UPDATE reservierungen SET status='Ausgecheckt' WHERE reservierungs_code=@code AND status='Eingecheckt'",
                @"UPDATE zimmer z
                  JOIN reservierungen r ON r.zimmer_id = z.id
                  SET z.status = 'Verfuegbar'
                  WHERE r.reservierungs_code = @code
                    AND r.status = 'Ausgecheckt';");
        }

        private void ExecuteStayTransition(ReservationItem reservation, string operation, string reservationSql, string roomSql)
        {
            var reservationCode = reservation.ReservationCode;
            var guestName = reservation.GastName;
            var committed = false;
            var commitAttempted = false;
            var businessRefused = false;
            var rollbackFailed = false;

            try
            {
                using var conn = DB.GetConnection();
                conn.Open();
                using var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                try
                {
                    using var cmd = new MySqlCommand(reservationSql, conn, transaction);
                    cmd.Parameters.AddWithValue("@code", reservationCode);
                    businessRefused = cmd.ExecuteNonQuery() != 1;
                    if (!businessRefused)
                    {
                        using var roomCmd = new MySqlCommand(roomSql, conn, transaction);
                        roomCmd.Parameters.AddWithValue("@code", reservationCode);
                        businessRefused = roomCmd.ExecuteNonQuery() != 1;
                        if (!businessRefused)
                        {
                            commitAttempted = true;
                            transaction.Commit();
                            committed = true;
                        }
                    }
                }
                finally
                {
                    if (!committed)
                    {
                        try
                        {
                            transaction.Rollback();
                        }
                        catch (Exception)
                        {
                            // Ein Rollback-Fehler darf den ursprünglichen Fehler nicht ersetzen.
                            rollbackFailed = true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                var message = committed
                    ? $"Der {operation} wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu."
                    : commitAttempted
                        ? $"Das Ergebnis des {operation} konnte nicht bestätigt werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen."
                        : $"Der {operation} konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.";
                System.Windows.MessageBox.Show(message, "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            if (rollbackFailed)
            {
                System.Windows.MessageBox.Show(
                    $"Der {operation} konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            if (businessRefused)
            {
                System.Windows.MessageBox.Show(
                    "Die Reservierung oder das zugehörige Zimmer konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Ab hier ist Commit bestätigt; UI-Fehler dürfen keinen Rollback auslösen.
            try
            {
                var reservationsLoaded = LoadReservationsFromDatabase(showError: false);
                var roomsLoaded = LoadRoomsFromDatabase(showError: false);
                if (!reservationsLoaded || !roomsLoaded)
                {
                    System.Windows.MessageBox.Show(
                        $"Der {operation} wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                        "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                System.Windows.MessageBox.Show($"{operation} für {guestName} erfolgreich!",
                    "Erfolg", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show(
                    $"Der {operation} wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        // ==================== STORNIEREN ====================
        public void CancelReservation()
        {
            if (SelectedReservation == null)
            {
                System.Windows.MessageBox.Show("Bitte eine Reservierung auswählen.", "Hinweis",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (SelectedReservation.Status != "Reserviert" && SelectedReservation.Status != "Eingecheckt")
            {
                System.Windows.MessageBox.Show("Nur aktive Reservierungen können storniert werden.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (System.Windows.MessageBox.Show(
                $"Reservierung für {SelectedReservation.GastName} wirklich stornieren?",
                "Stornieren", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
                != System.Windows.MessageBoxResult.Yes)
                return;

            using var conn = DB.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);

            try
            {
                using (var cmd = new MySqlCommand(
                    "UPDATE reservierungen SET status='Storniert' WHERE reservierungs_code=@code",
                    conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@code", SelectedReservation.ReservationCode);
                    cmd.ExecuteNonQuery();
                }

                using (var roomCmd = new MySqlCommand(
                    "UPDATE zimmer SET status='Verfuegbar' WHERE zimmer_nummer=@room",
                    conn, transaction))
                {
                    roomCmd.Parameters.AddWithValue("@room", SelectedReservation.RoomNumber);
                    roomCmd.ExecuteNonQuery();
                }

                transaction.Commit();

                System.Windows.MessageBox.Show("Reservierung storniert.", "Erfolg",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                LoadReservationsFromDatabase();
                LoadRoomsFromDatabase();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                System.Windows.MessageBox.Show($"Fehler beim Stornieren: {ex.Message}", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        // ==================== NAVIGATION ====================
        public ShellViewModel Shell => _shell;
        public void ShowGuests()
        {
            ActiveItem = new GastManagementViewModel();
        }

        public void ShowReservations()
        {
            ActiveItem = null;
        }

        public void ShowAllReservations()
        {
            ActiveItem = new AllBookingsViewModel();
        }
        public void ShowCustomerPortal()
        {
            ActiveItem = new CustomerPortalViewModel(_shell, null);
        }

        public void Logout()
        {
            _shell.WindowTitle = "Hotelverwaltung – Anmeldung";
            _shell.ShowLogin();
        }

        public void Exit()
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    // ==================== HILFSKLASSE ====================
    public class ReservationItem
    {
        public string ReservationCode { get; set; } = string.Empty;
        public string GastName { get; set; } = string.Empty;
        public string RoomNumber { get; set; } = string.Empty;
        public string RoomType { get; set; } = string.Empty;
        public DateTime CheckOut { get; set; }
        public int Nights { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
