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
using Nest.Synchronization;

namespace Nest.ViewModels.Reception
{
    public class ReservationManagementViewModel : BookingPollingScreen
    {
        private readonly ShellViewModel _shell;
        private readonly RoomRepository _roomRepository;
        private readonly ReservationRepository _reservationRepository;
        private readonly GastRepository _gastRepository;

        // ==================== KONSTRUKTOR ====================
        public ReservationManagementViewModel(ShellViewModel shell) : this(shell, () => PollingCoordinator.Shared) { }
        internal ReservationManagementViewModel(ShellViewModel shell, Func<PollingCoordinator> polling) : base(polling)
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
                var previous = _selectedRoom;
                _selectedRoom = value;
                DraftRoomWarning = "";
                NotifyOfPropertyChange(() => SelectedRoom);
                CalculatePrice();
                // Once the user leaves a pinned draft room, release that stale
                // option and show the latest server value (or remove it).
                if (previous != null && previous.Id != value?.Id && lastSnapshot != null)
                {
                    var index = RoomList.IndexOf(previous);
                    var latest = lastSnapshot.Rooms.FirstOrDefault(r => r.Id == previous.Id);
                    if (index >= 0)
                    {
                        if (latest == null) RoomList.RemoveAt(index);
                        else RoomList[index] = MapRoom(latest);
                    }
                }
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
            private set
            {
                _activeItem = value;
                NotifyOfPropertyChange(() => ActiveItem);
            }
        }

        // ==================== BERECHNUNGEN ====================
        private IReadOnlyList<BookingSnapshot> cachedBookings = Array.Empty<BookingSnapshot>();
        private PollingSnapshot? lastSnapshot;
        private bool switchingEmbedded;
        private string draftRoomWarning = "";
        public string DraftRoomWarning
        {
            get => draftRoomWarning;
            private set
            {
                if (draftRoomWarning == value) return;
                draftRoomWarning = value;
                NotifyOfPropertyChange(() => DraftRoomWarning);
            }
        }
        protected override bool PollingVisible => ActiveItem == null && !switchingEmbedded;
        private static Room MapRoom(RoomSnapshot r) => new()
        {
            Id = r.Id, RoomNumber = r.Number, Type = r.Type, PricePerNight = r.Price,
            Capacity = r.Capacity, Status = r.Status, Description = r.Description,
            ImagePath = r.ImagePath, DateAdded = r.Added, DateDelete = r.Deleted, OriginalValues = r.OriginalValues
        };
        protected override void ApplyPollingSnapshot(PollingSnapshot snapshot)
        {
            if (lastSnapshot != null && lastSnapshot.HasSameData(snapshot)) return;
            cachedBookings = snapshot.Bookings;
            ApplyReservationFilter();
            if (lastSnapshot == null || !lastSnapshot.Rooms.SequenceEqual(snapshot.Rooms))
            {
                // A chosen room is a draft value. Keep its object, price and selection
                // even if another employee changes/deletes it. Repository revalidates on Save.
                var selected = SelectedRoom;
                var rooms = snapshot.Rooms.Select(r => selected?.Id == r.Id ? selected : MapRoom(r)).ToList();
                if (selected != null && !rooms.Any(r => r.Id == selected.Id)) rooms.Add(selected);
                Reconcile(RoomList, rooms, r => r.Id, (a, b) => a.Id == b.Id && a.RoomNumber == b.RoomNumber &&
                    a.Type == b.Type && a.PricePerNight == b.PricePerNight && a.Capacity == b.Capacity &&
                    a.Status == b.Status && a.Description == b.Description && a.ImagePath == b.ImagePath &&
                    a.DateAdded == b.DateAdded && a.DateDelete == b.DateDelete);
                var current = snapshot.Rooms.FirstOrDefault(r => r.Id == selected?.Id);
                var draft = selected == null ? null : new RoomSnapshot(selected.Id, selected.RoomNumber, selected.Type,
                    selected.PricePerNight, selected.Capacity, selected.Status, selected.Description, selected.ImagePath,
                    selected.DateAdded, selected.DateDelete) { OriginalValues = selected.OriginalValues };
                DraftRoomWarning = draft != null && current != draft
                    ? "Das gewählte Zimmer wurde geändert oder entfernt. Entwurfswerte bleiben erhalten; bitte prüfen Sie das Zimmer vor dem Speichern." : "";
            }
            lastSnapshot = snapshot;
        }
        private void ApplyReservationFilter()
        {
            var code = SelectedReservation?.ReservationCode;
            var filtered = cachedBookings.Where(r => string.IsNullOrWhiteSpace(SearchText) ||
                r.Guest.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.Code.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            var incoming = filtered.Select(r => new ReservationItem
            {
                ReservationCode = r.Code, GastName = r.Guest, RoomNumber = r.Room, RoomType = r.Type,
                CheckOut = r.Departure, Nights = r.Nights, Amount = r.Total, Status = r.Status
            }).ToArray();
            Reconcile(Reservations, incoming, r => r.ReservationCode, (a, b) =>
                a.ReservationCode == b.ReservationCode && a.GastName == b.GastName && a.RoomNumber == b.RoomNumber &&
                a.RoomType == b.RoomType && a.CheckOut == b.CheckOut && a.Nights == b.Nights && a.Amount == b.Amount && a.Status == b.Status);
            var selected = Reservations.FirstOrDefault(r => r.ReservationCode == code);
            if (!ReferenceEquals(SelectedReservation, selected)) SelectedReservation = selected;
        }
        protected override async Task OnActivatedAsync(CancellationToken cancellationToken)
        {
            await base.OnActivatedAsync(cancellationToken);
            if (ActiveItem is IActivate child) await child.ActivateAsync(cancellationToken);
        }
        protected override async Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
        {
            await base.OnDeactivateAsync(close, cancellationToken);
            if (ActiveItem is IDeactivate child) await child.DeactivateAsync(close, cancellationToken);
        }
        private async Task ChangeEmbeddedScreen(Screen? next)
        {
            switchingEmbedded = true;
            UpdatePollingSubscription();
            try
            {
                if (ActiveItem is IDeactivate previous) await previous.DeactivateAsync(true);
                ActiveItem = next;
                if (PollingActive && next != null) await ((IActivate)next).ActivateAsync();
            }
            finally { switchingEmbedded = false; UpdatePollingSubscription(); }
        }

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
            if (PollingActive) { RequestPollingRefresh(); return true; }
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
            if (PollingActive) { RequestPollingRefresh(); return true; }
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
            ApplyReservationFilter();
        }

        public void ClearSearch()
        {
            SearchText = "";
        }

        // ==================== RESERVIERUNG SPEICHERN ====================
        public void SaveReservation()
        {
            using var pollingPause = PausePolling();
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

            var saved = false;
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

                saved = true;
                System.Windows.MessageBox.Show(
                    $"Reservierung gespeichert!\nBuchungsnummer: {reservierung.ReservationCode}\nGesamtpreis: {Price:C}\nAnzahlung: {Deposit:C}",
                    "Erfolg", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                var reservationsLoaded = LoadReservationsFromDatabase(showError: false);
                var roomsLoaded = LoadRoomsFromDatabase(showError: false);
                if (!reservationsLoaded || !roomsLoaded)
                {
                    System.Windows.MessageBox.Show(
                        "Die Reservierung wurde gespeichert, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                        "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                ResetForm();
            }
            catch (ReservationCommitUncertainException)
            {
                System.Windows.MessageBox.Show(
                    "Das Ergebnis der Buchung konnte nicht bestätigt werden. Bitte prüfen Sie die Reservierung, bevor Sie erneut buchen.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            catch (ArgumentException) when (!saved)
            {
                System.Windows.MessageBox.Show("Bitte prüfen Sie die Buchungsangaben, den Zeitraum und die Personenanzahl.",
                    "Ungültige Angaben", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show(saved
                    ? "Die Reservierung wurde gespeichert, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu."
                    : "Die Buchung konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie die Reservierung, bevor Sie erneut buchen.", "Fehler",
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
            using var pollingPause = PausePolling();
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
            using var pollingPause = PausePolling();
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
            StayTransitionResult result;
            try
            {
                result = _reservationRepository.ApplyStayTransition(reservationCode,
                    isCheckIn ? StayTransition.CheckIn : StayTransition.CheckOut);
            }
            catch (ReservationCommitUncertainException)
            {
                System.Windows.MessageBox.Show(
                    $"Das Ergebnis des {operation} konnte nicht bestätigt werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show(
                    $"Der {operation} konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            if (result == StayTransitionResult.CommittedWithCleanupFailure)
            {
                System.Windows.MessageBox.Show(
                    $"Der {operation} wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            if (result != StayTransitionResult.Success)
            {
                var businessRefusal = result switch
                {
                    StayTransitionResult.ReservationChanged => "Die Reservierung wurde nicht gefunden oder inzwischen geändert. Bitte laden Sie die Daten neu.",
                    StayTransitionResult.UnsuitableRoom => "Der Zimmerstatus ist für diesen Vorgang nicht geeignet oder widerspricht der Belegung. Bitte prüfen Sie die Daten.",
                    StayTransitionResult.OccupancyConflict => "Für dieses Zimmer ist bereits eine andere Reservierung eingecheckt. Der Vorgang wurde nicht ausgeführt. Bitte prüfen Sie die Belegung.",
                    StayTransitionResult.UpdateRefused => "Die Reservierung oder das zugehörige Zimmer konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                    _ => null
                };
                System.Windows.MessageBox.Show(
                    businessRefusal ?? $"Der {operation} konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    businessRefusal != null ? "Hinweis" : "Fehler", System.Windows.MessageBoxButton.OK,
                    businessRefusal != null ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Error);
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
            using var pollingPause = PausePolling();
            if (SelectedReservation == null)
            {
                System.Windows.MessageBox.Show("Bitte eine Reservierung auswählen.", "Hinweis",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (SelectedReservation.Status == "Eingecheckt")
            {
                System.Windows.MessageBox.Show("Die Reservierung ist bereits eingecheckt. Bitte führen Sie zuerst den Check-out durch.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            if (SelectedReservation.Status != "Reserviert")
            {
                System.Windows.MessageBox.Show("Nur Reservierungen mit Status 'Reserviert' können storniert werden.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (System.Windows.MessageBox.Show(
                $"Reservierung für {SelectedReservation.GastName} wirklich stornieren?",
                "Stornieren", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question)
                != System.Windows.MessageBoxResult.Yes)
                return;

            var reservationCode = SelectedReservation.ReservationCode;
            CancellationResult result;
            try
            {
                result = _reservationRepository.CancelReservation(reservationCode);
            }
            catch (ReservationCommitUncertainException)
            {
                System.Windows.MessageBox.Show(
                    "Das Ergebnis der Stornierung konnte nicht bestätigt werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            catch (ReservationRollbackFailedException ex) when (
                !ex.Data.Contains("TransactionDisposeException") && !ex.Data.Contains("ConnectionDisposeException"))
            {
                System.Windows.MessageBox.Show(
                    "Die Stornierung konnte nicht bestätigt werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show(
                    "Die Stornierung konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            if (result == CancellationResult.CommittedWithCleanupFailure)
            {
                System.Windows.MessageBox.Show(
                    "Die Stornierung wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                    "Fehler", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            if (result != CancellationResult.Success)
            {
                var businessRefusal = result switch
                {
                    CancellationResult.ReservationChangedOrNotFound => "Die Reservierung wurde nicht gefunden oder inzwischen geändert. Bitte laden Sie die Daten neu.",
                    CancellationResult.RoomNotFound => "Das zugehörige Zimmer wurde nicht gefunden. Bitte laden Sie die Daten neu.",
                    CancellationResult.RequiresCheckOut => "Die Reservierung ist bereits eingecheckt. Bitte führen Sie zuerst den Check-out durch.",
                    _ => null
                };
                System.Windows.MessageBox.Show(
                    businessRefusal ?? "Die Stornierung konnte aufgrund eines technischen Fehlers nicht abgeschlossen werden. Bitte prüfen Sie den tatsächlichen Reservierungsstatus, bevor Sie den Vorgang erneut ausführen.",
                    businessRefusal != null ? "Hinweis" : "Fehler", System.Windows.MessageBoxButton.OK,
                    businessRefusal != null ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Error);
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
                        "Die Stornierung wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                        "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                System.Windows.MessageBox.Show("Reservierung storniert.", "Erfolg",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception)
            {
                System.Windows.MessageBox.Show(
                    "Die Stornierung wurde ausgeführt, aber die Anzeige konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                    "Hinweis", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        // ==================== NAVIGATION ====================
        public ShellViewModel Shell => _shell;
        public Task ShowGuests()
        {
            return ChangeEmbeddedScreen(new GastManagementViewModel());
        }

        public Task ShowReservations()
        {
            return ChangeEmbeddedScreen(null);
        }

        public Task ShowAllReservations()
        {
            return ChangeEmbeddedScreen(new AllBookingsViewModel());
        }
        public Task ShowCustomerPortal()
        {
            return ChangeEmbeddedScreen(new CustomerPortalViewModel(_shell, null));
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
