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
        private void LoadRoomsFromDatabase()
        {
            try
            {
                var zimmer = _roomRepository.GetAll();
                RoomList.Clear();
                foreach (var room in zimmer)
                    RoomList.Add(room);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Fehler beim Laden der Zimmer: {ex.Message}",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadReservationsFromDatabase()
        {
            FilterReservations();
        }

        public void FilterReservations()
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

                Reservations.Clear();
                foreach (var r in data)
                {
                    Reservations.Add(new ReservationItem
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
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Fehler beim Laden der Reservierungen: {ex.Message}",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void ClearSearch()
        {
            SearchText = "";
        }

        // ==================== GAST SPEICHERN / FINDEN ====================
        private int SaveOrGetGuest()
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string checkSql = @"
                SELECT id FROM gaeste 
                WHERE email = @email 
                OR (vorname = @firstName AND nachname = @lastName AND telefon = @telefon)
                LIMIT 1";

            using var checkCmd = new MySqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("@email", (object?)Email ?? DBNull.Value);
            checkCmd.Parameters.AddWithValue("@firstName", FirstName);
            checkCmd.Parameters.AddWithValue("@lastName", LastName);
            checkCmd.Parameters.AddWithValue("@telefon", (object?)Phone ?? DBNull.Value);

            var result = checkCmd.ExecuteScalar();
            if (result != null)
                return Convert.ToInt32(result);

            string insertSql = @"
                INSERT INTO gaeste (vorname, nachname, email, telefon, ausweis_typ, ausweis_nummer, nationalitaet)
                VALUES (@firstName, @lastName, @email, @telefon, @idType, @idNumber, @nationalitaet);
                SELECT LAST_INSERT_ID();";

            using var insertCmd = new MySqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("@firstName", FirstName);
            insertCmd.Parameters.AddWithValue("@lastName", LastName);
            insertCmd.Parameters.AddWithValue("@email", (object?)Email ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@telefon", (object?)Phone ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@idType", (object?)IdType ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@idNumber", (object?)IdNumber ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@nationalitaet", (object?)Nationality ?? DBNull.Value);

            return Convert.ToInt32(insertCmd.ExecuteScalar());
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
            if (CheckInDate >= CheckOutDate)
            {
                System.Windows.MessageBox.Show("Check-out muss nach Check-in liegen!", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            try
            {
                int guestId = SaveOrGetGuest();
                int roomId = SelectedRoom.Id;

                using var conn = DB.GetConnection();
                conn.Open();

                string code = "BNR-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                int naechte = (CheckOutDate - CheckInDate).Days;

                string sql = @"
                    INSERT INTO reservierungen 
                    (reservierungs_code, gast_id, zimmer_id, anreise_datum, abreise_datum, 
                     naechte, erwachsene, kinder, gesamtbetrag, zahlungsart, status, sonderwuensche)
                    VALUES 
                    (@code, @guestId, @roomId, @checkIn, @checkOut, 
                     @naechte, @erwachsene, @kinder, @total, @payment, 'Reserviert', @requests)";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@code", code);
                cmd.Parameters.AddWithValue("@guestId", guestId);
                cmd.Parameters.AddWithValue("@roomId", roomId);
                cmd.Parameters.AddWithValue("@checkIn", CheckInDate.Date);
                cmd.Parameters.AddWithValue("@checkOut", CheckOutDate.Date);
                cmd.Parameters.AddWithValue("@naechte", naechte);
                cmd.Parameters.AddWithValue("@erwachsene", Adults);
                cmd.Parameters.AddWithValue("@kinder", Children);
                cmd.Parameters.AddWithValue("@total", Price);
                cmd.Parameters.AddWithValue("@payment", PaymentType);
                cmd.Parameters.AddWithValue("@requests", (object?)SpecialRequests ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                using var updateRoom = new MySqlCommand(
                    "UPDATE zimmer SET status='Reserviert' WHERE id=@id", conn);
                updateRoom.Parameters.AddWithValue("@id", roomId);
                updateRoom.ExecuteNonQuery();

                System.Windows.MessageBox.Show(
                    $"Reservierung gespeichert!\nBuchungsnummer: {code}\nGesamtpreis: {Price:C}",
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

            using var conn = DB.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);

            try
            {
                using (var cmd = new MySqlCommand(
                    "UPDATE reservierungen SET status='Eingecheckt' WHERE reservierungs_code=@code AND status='Reserviert'",
                    conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@code", SelectedReservation.ReservationCode);
                    if (cmd.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        System.Windows.MessageBox.Show("Check-in fehlgeschlagen.", "Fehler",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                }

                using (var roomCmd = new MySqlCommand(
                    "UPDATE zimmer SET status='Belegt' WHERE zimmer_nummer=@room",
                    conn, transaction))
                {
                    roomCmd.Parameters.AddWithValue("@room", SelectedReservation.RoomNumber);
                    roomCmd.ExecuteNonQuery();
                }

                transaction.Commit();

                System.Windows.MessageBox.Show($"Check-in für {SelectedReservation.GastName} erfolgreich!",
                    "Erfolg", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                LoadReservationsFromDatabase();
                LoadRoomsFromDatabase();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                System.Windows.MessageBox.Show($"Fehler beim Check-in: {ex.Message}", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
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

            using var conn = DB.GetConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);

            try
            {
                using (var cmd = new MySqlCommand(
                    "UPDATE reservierungen SET status='Ausgecheckt' WHERE reservierungs_code=@code AND status='Eingecheckt'",
                    conn, transaction))
                {
                    cmd.Parameters.AddWithValue("@code", SelectedReservation.ReservationCode);
                    if (cmd.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        System.Windows.MessageBox.Show("Check-out fehlgeschlagen.", "Fehler",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return;
                    }
                }

                using (var roomCmd = new MySqlCommand(
                    "UPDATE zimmer SET status='Verfuegbar' WHERE zimmer_nummer=@room",
                    conn, transaction))
                {
                    roomCmd.Parameters.AddWithValue("@room", SelectedReservation.RoomNumber);
                    roomCmd.ExecuteNonQuery();
                }

                transaction.Commit();

                System.Windows.MessageBox.Show($"Check-out für {SelectedReservation.GastName} erfolgreich!",
                    "Erfolg", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                LoadReservationsFromDatabase();
                LoadRoomsFromDatabase();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                System.Windows.MessageBox.Show($"Fehler beim Check-out: {ex.Message}", "Fehler",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
            _shell.WindowTitle = "Hotel Management System - Login";
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
