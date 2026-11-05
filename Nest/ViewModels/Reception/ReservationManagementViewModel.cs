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

            ExecuteStayTransition(SelectedReservation, isCheckIn: true);
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

            ExecuteStayTransition(SelectedReservation, isCheckIn: false);
        }

        private void ExecuteStayTransition(ReservationItem reservation, bool isCheckIn)
        {
            var operation = isCheckIn ? "Check-in" : "Check-out";
            var reservationCode = reservation.ReservationCode;
            var guestName = reservation.GastName;
            var committed = false;
            var commitAttempted = false;
            string? businessRefusal = null;
            var rollbackFailed = false;

            try
            {
                using var conn = DB.GetConnection();
                conn.Open();
                using var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                try
                {
                    businessRefusal = ApplyStayTransition(conn, transaction, reservationCode, isCheckIn);
                    if (businessRefusal == null)
                    {
                        commitAttempted = true;
                        transaction.Commit();
                        committed = true;
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
            if (businessRefusal != null)
            {
                System.Windows.MessageBox.Show(
                    businessRefusal,
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

        private static string? ApplyStayTransition(MySqlConnection conn, MySqlTransaction transaction,
            string reservationCode, bool isCheckIn)
        {
            const string changedReservation = "Die Reservierung wurde nicht gefunden oder inzwischen geändert. Bitte laden Sie die Daten neu.";
            const string unsuitableRoom = "Der Zimmerstatus ist für diesen Vorgang nicht geeignet oder widerspricht der Belegung. Bitte prüfen Sie die Daten.";
            const string occupancyConflict = "Für dieses Zimmer ist bereits eine andere Reservierung eingecheckt. Der Vorgang wurde nicht ausgeführt. Bitte prüfen Sie die Belegung.";
            const string updateRefused = "Die Reservierung oder das zugehörige Zimmer konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.";

            // Nur Kandidatensuche: noch keine Reservierungssperre vor der Zimmersperre.
            const string findRoomSql = @"
                SELECT zimmer_id FROM reservierungen
                WHERE reservierungs_code = @code;";
            int roomId;
            using (var findRoom = new MySqlCommand(findRoomSql, conn, transaction))
            {
                findRoom.Parameters.AddWithValue("@code", reservationCode);
                var value = findRoom.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                    return changedReservation;
                roomId = Convert.ToInt32(value);
            }

            // Gemeinsamer Serialisierungspunkt für Check-in und Check-out desselben Zimmers.
            const string lockRoomSql = @"
                SELECT id, status, geloescht_am FROM zimmer
                WHERE id = @RoomId FOR UPDATE;";
            string roomStatus;
            bool roomDeleted;
            using (var lockRoom = new MySqlCommand(lockRoomSql, conn, transaction))
            {
                lockRoom.Parameters.AddWithValue("@RoomId", roomId);
                using var reader = lockRoom.ExecuteReader();
                if (!reader.Read() || reader.GetInt32("id") != roomId)
                    return unsuitableRoom;
                roomStatus = reader.GetString("status");
                roomDeleted = !reader.IsDBNull(reader.GetOrdinal("geloescht_am"));
            }

            const string lockReservationSql = @"
                SELECT id, reservierungs_code, zimmer_id, status FROM reservierungen
                WHERE reservierungs_code = @code FOR UPDATE;";
            int reservationId;
            var sourceStatus = isCheckIn ? "Reserviert" : "Eingecheckt";
            var targetStatus = isCheckIn ? "Eingecheckt" : "Ausgecheckt";
            using (var lockReservation = new MySqlCommand(lockReservationSql, conn, transaction))
            {
                lockReservation.Parameters.AddWithValue("@code", reservationCode);
                using var reader = lockReservation.ExecuteReader();
                if (!reader.Read()
                    || !string.Equals(reader.GetString("reservierungs_code"), reservationCode, StringComparison.Ordinal)
                    || reader.GetInt32("zimmer_id") != roomId
                    || reader.GetString("status") != sourceStatus)
                    return changedReservation;
                reservationId = reader.GetInt32("id");
            }

            const string otherOccupantsSql = @"
                SELECT id FROM reservierungen
                WHERE zimmer_id = @RoomId AND status = 'Eingecheckt'
                  AND id <> @ReservationId
                ORDER BY id FOR UPDATE;";
            using (var otherOccupants = new MySqlCommand(otherOccupantsSql, conn, transaction))
            {
                otherOccupants.Parameters.AddWithValue("@RoomId", roomId);
                otherOccupants.Parameters.AddWithValue("@ReservationId", reservationId);
                if (otherOccupants.ExecuteScalar() != null)
                    return occupancyConflict;
            }

            if (isCheckIn)
            {
                if (roomDeleted || (roomStatus != "Verfuegbar" && roomStatus != "Reserviert"))
                    return unsuitableRoom;
            }
            else if (roomStatus != "Belegt" && roomStatus != "Wartung")
            {
                return unsuitableRoom;
            }

            const string updateReservationSql = @"
                UPDATE reservierungen SET status = @TargetStatus
                WHERE id = @ReservationId AND reservierungs_code = @code
                  AND zimmer_id = @RoomId AND status = @SourceStatus;";
            using (var updateReservation = new MySqlCommand(updateReservationSql, conn, transaction))
            {
                updateReservation.Parameters.AddWithValue("@TargetStatus", targetStatus);
                updateReservation.Parameters.AddWithValue("@ReservationId", reservationId);
                updateReservation.Parameters.AddWithValue("@code", reservationCode);
                updateReservation.Parameters.AddWithValue("@RoomId", roomId);
                updateReservation.Parameters.AddWithValue("@SourceStatus", sourceStatus);
                if (updateReservation.ExecuteNonQuery() != 1)
                    return updateRefused;
            }

            // Wartung und weiche Löschung bleiben beim Check-out unangetastet.
            if (!isCheckIn && (roomDeleted || roomStatus == "Wartung"))
                return null;

            const string updateRoomSql = @"
                UPDATE zimmer z
                JOIN reservierungen r ON r.zimmer_id = z.id
                SET z.status = @TargetRoomStatus
                WHERE z.id = @RoomId AND r.id = @ReservationId
                  AND r.reservierungs_code = @code AND r.status = @TargetStatus
                  AND z.status = @SourceRoomStatus AND z.geloescht_am IS NULL;";
            using (var updateRoom = new MySqlCommand(updateRoomSql, conn, transaction))
            {
                updateRoom.Parameters.AddWithValue("@TargetRoomStatus", isCheckIn ? "Belegt" : "Verfuegbar");
                updateRoom.Parameters.AddWithValue("@RoomId", roomId);
                updateRoom.Parameters.AddWithValue("@ReservationId", reservationId);
                updateRoom.Parameters.AddWithValue("@code", reservationCode);
                updateRoom.Parameters.AddWithValue("@TargetStatus", targetStatus);
                updateRoom.Parameters.AddWithValue("@SourceRoomStatus", roomStatus);
                return updateRoom.ExecuteNonQuery() == 1 ? null : updateRefused;
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
