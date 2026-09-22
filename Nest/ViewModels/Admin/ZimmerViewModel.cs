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

namespace Nest.ViewModels.Admin
{
    public class ZimmerViewModel : Screen
    {
        private readonly RoomRepository _roomRepository;

        public ZimmerViewModel()
        {
            _roomRepository = new RoomRepository();
            LoadRooms();
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
        private ObservableCollection<Room> _rooms;

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
            var zimmer = _roomRepository.GetAll();
            Rooms = new ObservableCollection<Room>(zimmer);
        }

        // Zimmer selected
        private Room _selectedRoom;

        public Room SelectedRoom
        {
            get => _selectedRoom;
            set
            {
                _selectedRoom = value;
                NotifyOfPropertyChange(() => SelectedRoom);

                if (_selectedRoom != null)
                {
                    RoomNumber = _selectedRoom.RoomNumber;
                    Capacity = _selectedRoom.Capacity;
                    PriceText = _selectedRoom.PricePerNight.ToString("0.00");
                    SelectedTypeIndex = GetTypeIndex(_selectedRoom.Type);
                    SelectedStatusIndex = GetStatusIndex(_selectedRoom.Status);
                    Beschreibung = _selectedRoom.Description;
                }
            }
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

            try
            {
                _roomRepository.Add(room);

                LoadRooms();
                ClearForm();

                MessageBox.Show(
                    "Zimmer erfolgreich hinzugefügt!",
                    "Erfolg",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                MessageBox.Show(
                    "Zimmernummer bereits vergeben!",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            LoadRooms();
        }

        //Button löschen
        public void Löschen()
        {
            if (SelectedRoom == null)
                return;

            var result = MessageBox.Show(
                "Zimmer wirklich löschen?",
                "Bestätigung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            bool deleted = _roomRepository.Delete(SelectedRoom.Id);

            if (!deleted)
            {
                MessageBox.Show(
                    "Das Zimmer kann nicht gelöscht werden, da dafür bereits Reservierungen vorhanden sind.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            LoadRooms();
        }

        // Button aktualisieren
        public void Aktualisieren()
        {
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

            try
            {
                _roomRepository.Update(room);

                LoadRooms();
                ClearForm();

                MessageBox.Show(
                    "Zimmer aktualisiert!",
                    "Erfolg",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                MessageBox.Show(
                    "Zimmernummer bereits vergeben!",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            LoadRooms();
        }
    }
}
