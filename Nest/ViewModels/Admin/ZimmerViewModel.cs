using Caliburn.Micro;
using MySql.Data.MySqlClient;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Nest.Synchronization;
using Nest.ViewModels.Reception;

namespace Nest.ViewModels.Admin
{
    public class ZimmerViewModel : BookingPollingScreen
    {
        private readonly RoomRepository _roomRepository;
        private readonly Func<Room, RoomWriteResult> addRoom;
        private readonly Func<Room, RoomOriginalValues, RoomWriteResult> updateRoom;
        private readonly Func<RoomOriginalValues, RoomWriteResult> deleteRoom;
        private readonly Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult> dialog;
        private IReadOnlyList<RoomSnapshot>? latestRooms;
        private RoomSnapshot? editBaseline;
        private int? editingRoomId;
        private bool applyingSnapshot;
        private bool missingRoom;
        private bool changedRoom;
        private bool repositoryConflict;
        private string roomConflictWarning = "";
        public string RoomConflictWarning
        {
            get => roomConflictWarning;
            private set
            {
                if (roomConflictWarning == value) return;
                roomConflictWarning = value;
                NotifyOfPropertyChange(() => RoomConflictWarning);
            }
        }
        public bool CanAktualisieren => SelectedRoom != null && !missingRoom && !changedRoom;
        public bool CanLöschen => CanAktualisieren;
        public bool CanReloadSelectedRoom => editingRoomId != null && !missingRoom;

        public ZimmerViewModel() : this(() => PollingCoordinator.Shared) { }
        internal ZimmerViewModel(Func<PollingCoordinator> polling,
            Func<Room, RoomWriteResult>? add = null, Func<Room, RoomOriginalValues, RoomWriteResult>? update = null,
            Func<RoomOriginalValues, RoomWriteResult>? delete = null,
            Func<string, string, MessageBoxButton, MessageBoxImage, MessageBoxResult>? showDialog = null) : base(polling)
        {
            _roomRepository = new RoomRepository();
            addRoom = add ?? (room => _roomRepository.Add(room));
            updateRoom = update ?? ((room, expected) => _roomRepository.Update(room, expected));
            deleteRoom = delete ?? (expected => _roomRepository.Delete(expected));
            dialog = showDialog ?? ((text, title, buttons, icon) => MessageBox.Show(text, title, buttons, icon));
        }

        protected override void ApplyPollingSnapshot(PollingSnapshot snapshot)
        {
            if (latestRooms != null && latestRooms.SequenceEqual(snapshot.Rooms)) return;
            var selectedId = SelectedRoom?.Id;
            latestRooms = snapshot.Rooms;
            applyingSnapshot = true;
            try
            {
                var rows = snapshot.Rooms.Select(MapRoom).ToArray();
                Reconcile(Rooms, rows, r => r.Id, (a, b) => Capture(a) == Capture(b));
                // Bypass the form-filling setter. A deleted row loses selection,
                // but its edit identity/baseline and the complete draft survive.
                _selectedRoom = Rooms.FirstOrDefault(r => r.Id == selectedId);
                NotifyOfPropertyChange(() => SelectedRoom);
            }
            finally { applyingSnapshot = false; }
            UpdateConflict();
        }
        private static RoomSnapshot Capture(Room r) => new(r.Id, r.RoomNumber, r.Type, r.PricePerNight,
            r.Capacity, r.Status, r.Description, r.ImagePath, r.DateAdded, r.DateDelete) { OriginalValues = r.OriginalValues };
        private static Room MapRoom(RoomSnapshot r) => new()
        {
            Id = r.Id, RoomNumber = r.Number, Type = r.Type, PricePerNight = r.Price,
            Capacity = r.Capacity, Status = r.Status, Description = r.Description,
            ImagePath = r.ImagePath, DateAdded = r.Added, DateDelete = r.Deleted, OriginalValues = r.OriginalValues
        };
        private void UpdateConflict()
        {
            var current = latestRooms?.FirstOrDefault(r => r.Id == editingRoomId);
            missingRoom = editingRoomId != null && current == null;
            changedRoom = editingRoomId != null && !missingRoom &&
                (repositoryConflict || editBaseline != current || editBaseline?.OriginalValues == null);
            RoomConflictWarning = missingRoom
                ? "Das bearbeitete Zimmer wurde entfernt. Ihre Eingaben bleiben erhalten; Aktualisieren und Löschen sind gesperrt."
                : changedRoom
                    ? "Das Zimmer wurde außerhalb dieses Entwurfs geändert. Ihre Eingaben bleiben erhalten. Prüfen Sie die Liste und übernehmen Sie den aktuellen Stand ausdrücklich, bevor Sie weiter speichern."
                    : "";
            NotifyOfPropertyChange(() => CanAktualisieren);
            NotifyOfPropertyChange(() => CanLöschen);
            NotifyOfPropertyChange(() => CanReloadSelectedRoom);
        }
        private bool HasKnownConflict()
        {
            if (!missingRoom && !changedRoom) return false;
            dialog(RoomConflictWarning, "Bearbeitungskonflikt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return true;
        }
        public void ReloadSelectedRoom()
        {
            using var pollingPause = PausePolling();
            var current = Rooms.FirstOrDefault(r => r.Id == editingRoomId);
            if (current == null) return;
            if (dialog("Aktuellen Zimmerstand in das Formular übernehmen? Ihre ungespeicherten Eingaben werden verworfen.",
                "Aktuellen Stand übernehmen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _selectedRoom = current;
            NotifyOfPropertyChange(() => SelectedRoom);
            BeginEditing(current); // Explicit user decision, never a polling callback.
        }

        // Property für ZimmerNumber
        private int? _roomNumber;
        public int? RoomNumber
        {
            get => _roomNumber;
            set
            {
                _roomNumber = value;
                NotifyOfPropertyChange(nameof(RoomNumber));
            }
        }

        // Property für ZimmerTyp
        private int _selectedTypeIndex = 0;

        public int SelectedTypeIndex
        {
            get => _selectedTypeIndex;
            set
            {
                _selectedTypeIndex = value;
                NotifyOfPropertyChange(nameof(SelectedTypeIndex));
            }
        }

        // Property für Capacity
        private int _capacity = 1;

        public int Capacity
        {
            get => _capacity;
            set
            {
                if (value < 1)
                    value = 1;

                if (value > 10)
                    value = 10;

                _capacity = value;
                NotifyOfPropertyChange(nameof(Capacity));
                NotifyOfPropertyChange(nameof(CanPersonUp));
                NotifyOfPropertyChange(nameof(CanPersonDown));
            }
        }

        public void PersonUp()
        {
            if (Capacity < 10)
                Capacity++;
        }

        public void PersonDown()
        {
            if (Capacity > 1)
                Capacity--;
        }

        public bool CanPersonUp => Capacity < 10;

        public bool CanPersonDown => Capacity > 1;


        // Preis
        private string _priceText = "0,00";

        public string PriceText
        {
            get => _priceText;
            set
            {
                _priceText = value;
                NotifyOfPropertyChange(nameof(PriceText));
                NotifyOfPropertyChange(nameof(CanPreisUp));
                NotifyOfPropertyChange(nameof(CanPreisDown));
            }
        }

        private decimal GetPrice()
        {
            string text = PriceText.Replace('.', ',');

            if (decimal.TryParse(
                    text,
                    NumberStyles.Any,
                    CultureInfo.GetCultureInfo("de-DE"),
                    out decimal result))
                return result;

            return 0;
        }

        public void PreisUp()
        {
            decimal preis = GetPrice();

            if (preis < 9999.99m)
            {
                preis += 1.00m;

                if (preis > 9999.99m)
                    preis = 9999.99m;

                PriceText = preis.ToString("F2", CultureInfo.GetCultureInfo("de-DE"));
            }
        }

        public void PreisDown()
        {
            decimal preis = GetPrice();

            if (preis > 0)
            {
                preis -= 1.00m;

                if (preis < 0)
                    preis = 0;

                PriceText = preis.ToString("F2", CultureInfo.GetCultureInfo("de-DE"));
            }
        }

        public void FormatPrice()
        {
            decimal preis = GetPrice();

            if (preis < 0)
                preis = 0;

            if (preis > 9999.99m)
                preis = 9999.99m;

            PriceText = preis.ToString("F2", CultureInfo.GetCultureInfo("de-DE"));
        }

        public bool CanPreisUp
        {
            get
            {
                return GetPrice() < 9999.99m;
            }
        }


        public bool CanPreisDown
        {
            get
            {
                return GetPrice() > 0;
            }
        }

        // Property für Status
        private int _selectedStatusIndex = 0;
        public int SelectedStatusIndex
        {
            get => _selectedStatusIndex;
            set
            {
                _selectedStatusIndex=value;
                NotifyOfPropertyChange(nameof(SelectedStatusIndex));
            }
        }

        // Property für Beschreibung
        private string? _beschreibung;
        public string? Beschreibung
        {
            get => _beschreibung;
            set
            {
                _beschreibung = value;
                NotifyOfPropertyChange(nameof(Beschreibung));
            }
        }



        // ===== ROOMS =====
        private ObservableCollection<Room> _rooms = new();

        public ObservableCollection<Room> Rooms
        {
            get => _rooms;
            set
            {
                _rooms = value;
                NotifyOfPropertyChange(() => Rooms);
            }
        }

        public void LoadRooms()
        {
            RequestPollingRefresh();
        }

        // Zimmer selected
        private Room? _selectedRoom;

        public Room? SelectedRoom
        {
            get => _selectedRoom;
            set
            {
                if (ReferenceEquals(_selectedRoom, value)) return;
                _selectedRoom = value;
                NotifyOfPropertyChange(() => SelectedRoom);
                if (applyingSnapshot) return;
                BeginEditing(value);
            }
        }
        private void BeginEditing(Room? room)
        {
            editingRoomId = room?.Id;
            editBaseline = room == null ? null : Capture(room);
            repositoryConflict = false;
            if (room != null)
            {
                RoomNumber = room.RoomNumber;
                Capacity = room.Capacity;
                PriceText = room.PricePerNight.ToString("0.00");
                SelectedTypeIndex = GetTypeIndex(room.Type);
                SelectedStatusIndex = GetStatusIndex(room.Status);
                Beschreibung = room.Description;
            }
            UpdateConflict();
        }

        private int GetTypeIndex(string type)
        {
            return type switch
            {
                "Standard" => 0,
                "Deluxe" => 1,
                "Suite" => 2,
                "Executive" => 3,
                _ => 0
            };
        }

        private int GetStatusIndex(string status)
        {
            return status switch
            {
                "Verfuegbar" => 0,
                "Belegt" => 1,
                "Wartung" => 2,
                "Reserviert" => 3,
                _ => 0
            };
        }

        private string GetType(int index)
        {
            return index switch
            {
                0 => "Standard",
                1 => "Deluxe",
                2 => "Suite",
                3 => "Executive",
                _ => "Standard"
            };
        }

        private string GetStatus(int index)
        {
            return index switch
            {
                0 => "Verfuegbar",
                1 => "Belegt",
                2 => "Wartung",
                3 => "Reserviert",
                _ => "Verfuegbar"
            };
        }

        // Buttons
        public void ClearForm()
        {
            using var pollingPause = PausePolling();
            SelectedRoom = null;
            BeginEditing(null);
            RoomNumber = null;
            Capacity = 1;
            PriceText = "0,00";
            Beschreibung = "";
            SelectedTypeIndex = 0;
            SelectedStatusIndex = 0;
        }

        // Button hinzufügen
        public void Hinzufügen()
        {
            using var pollingPause = PausePolling();
            if (RoomNumber == null)
                return;

            if (string.IsNullOrWhiteSpace(PriceText))
                return;

            if (string.IsNullOrWhiteSpace(Beschreibung))
                return;

            decimal preis;

            if (!decimal.TryParse(
                    PriceText,
                    NumberStyles.Number,
                    CultureInfo.GetCultureInfo("de-DE"),
                    out preis))
            {
                return;
            }

            var room = new Room
            {
                RoomNumber = RoomNumber.Value,
                Type = GetType(SelectedTypeIndex),
                PricePerNight = preis,
                Capacity = Capacity,
                Status = GetStatus(SelectedStatusIndex),
                Description = Beschreibung
            };

            ExecuteRoomWrite(() => addRoom(room), "Zimmer erfolgreich hinzugefügt!", clearForm: true);
        }

        //Button löschen
        public void Löschen()
        {
            using var pollingPause = PausePolling();
            if (HasKnownConflict()) return;
            if (SelectedRoom == null)
                return;

            var result = dialog(
                "Zimmer wirklich löschen?",
                "Bestätigung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            var expected = editBaseline?.OriginalValues;
            if (expected == null) return; // Never synthesize an SQL baseline from form/display values.
            ExecuteRoomWrite(() => deleteRoom(expected), "Zimmer gelöscht.", clearForm: false);
        }

        // Button aktualisieren
        public void Aktualisieren()
        {
            using var pollingPause = PausePolling();
            if (HasKnownConflict()) return;
            if (SelectedRoom == null)
                return;

            if (RoomNumber == null)
                return;

            if (string.IsNullOrWhiteSpace(PriceText))
                return;

            decimal preis;

            if (!decimal.TryParse(
                    PriceText,
                    NumberStyles.Number,
                    CultureInfo.GetCultureInfo("de-DE"),
                    out preis))
            {
                return;
            }

            var room = new Room
            {
                Id = SelectedRoom.Id,
                RoomNumber = RoomNumber.Value,
                Type = GetType(SelectedTypeIndex),
                PricePerNight = preis,
                Capacity = Capacity,
                Status = GetStatus(SelectedStatusIndex),
                Description = Beschreibung
            };

            var expected = editBaseline?.OriginalValues;
            if (expected == null) return;
            ExecuteRoomWrite(() => updateRoom(room, expected), "Zimmer aktualisiert!", clearForm: true);
        }

        private void ExecuteRoomWrite(Func<RoomWriteResult> write, string successMessage, bool clearForm)
        {
            RoomWriteResult result;
            try
            {
                result = write();
            }
            catch (Exception)
            {
                result = RoomWriteResult.TechnicalFailure;
            }
            if (result == RoomWriteResult.ConcurrencyConflict)
            {
                // Remain blocked even if the next snapshot happens to equal the old one (ABA).
                // Only an explicit acceptance/new selection establishes another baseline.
                repositoryConflict = true;
                UpdateConflict();
                RequestPollingRefresh();
                return;
            }
            if (result != RoomWriteResult.Success && result != RoomWriteResult.CommittedWithCleanupFailure)
            {
                var message = result switch
                {
                    RoomWriteResult.Missing => "Das Zimmer wurde nicht gefunden oder inzwischen gelöscht. Bitte laden Sie die Zimmerliste neu.",
                    RoomWriteResult.Occupied => "In diesem Zimmer ist ein Gast eingecheckt. Der Status muss 'Belegt' bleiben; das Zimmer kann nicht gelöscht werden.",
                    RoomWriteResult.FutureReservation => "Das Zimmer hat noch gültige Reservierungen und kann nicht gelöscht werden.",
                    RoomWriteResult.InvalidStatus => "Dieser Zimmerstatus ist nicht zulässig. 'Belegt' darf nur bei einer eingecheckten Reservierung gesetzt werden.",
                    RoomWriteResult.NumberConflict => "Zimmernummer bereits vergeben!",
                    RoomWriteResult.UncertainOutcome => "Das Ergebnis konnte nicht bestätigt werden. Bitte prüfen Sie den tatsächlichen Zimmerzustand, bevor Sie den Vorgang erneut ausführen.",
                    RoomWriteResult.CommitUncertain => "Das Speicherergebnis konnte nicht bestätigt werden. Bitte prüfen Sie zuerst den tatsächlichen Zimmerzustand. Wiederholen Sie den Vorgang nicht ungeprüft.",
                    _ => "Das Zimmer konnte aufgrund eines technischen Fehlers nicht gespeichert werden. Bitte prüfen Sie den tatsächlichen Zimmerzustand, bevor Sie den Vorgang erneut ausführen."
                };
                dialog(message, "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Erst nach bestätigtem Commit laden. Bei Ladefehlern bleiben Liste, Auswahl und Formular erhalten.
            try
            {
                RequestPollingRefresh();
                if (clearForm)
                    ClearForm();
                if (result == RoomWriteResult.CommittedWithCleanupFailure)
                    dialog("Die Änderung wurde gespeichert, aber beim Freigeben der Verbindung ist ein Fehler aufgetreten. Bitte prüfen Sie die aktualisierte Liste; führen Sie die Änderung nicht erneut aus.",
                        "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    dialog(successMessage, "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception)
            {
                dialog(
                    "Die Änderung wurde gespeichert, aber die Zimmerliste konnte nicht aktualisiert werden. Bitte laden Sie die Daten neu.",
                    "Hinweis", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
