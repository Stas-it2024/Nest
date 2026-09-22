using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.ViewModels.Admin
{
    public class DashboardViewModel : Screen
    {
        private readonly RoomRepository _roomRepository;
        private readonly ReservationRepository _reservationRepository;
        private readonly GastRepository _gastRepository;

        public DashboardViewModel()
        {
            _roomRepository = new RoomRepository();
            _reservationRepository = new ReservationRepository();
            _gastRepository = new GastRepository();

            LoadRooms();
            LoadReservations();
            LoadGuests();
        }

        // Für Dashboard
        private int _roomCount;
        public int RoomCount
        {
            get => _roomCount;
            set
            {
                _roomCount = value;
                NotifyOfPropertyChange(() => RoomCount);
            }
        }

        private int _availableRoomCount;
        public int AvailableRoomCount
        {
            get => _availableRoomCount;
            set
            {
                _availableRoomCount = value;
                NotifyOfPropertyChange(() => AvailableRoomCount);
            }
        }

        private int _occupiedRoomCount;
        public int OccupiedRoomCount
        {
            get => _occupiedRoomCount;
            set
            {
                _occupiedRoomCount = value;
                NotifyOfPropertyChange(() => OccupiedRoomCount);
            }
        }


        private int _reservedRoomCount;
        public int ReservedRoomCount
        {
            get => _reservedRoomCount;
            set
            {
                _reservedRoomCount = value;
                NotifyOfPropertyChange(() => ReservedRoomCount);
            }
        }


        private int _todayCheckInCount;
        public int TodayCheckInCount
        {
            get => _todayCheckInCount;
            set
            {
                _todayCheckInCount = value;
                NotifyOfPropertyChange(() => TodayCheckInCount);
            }
        }


        private int _todayCheckOutCount;
        public int TodayCheckOutCount
        {
            get => _todayCheckOutCount;
            set
            {
                _todayCheckOutCount = value;
                NotifyOfPropertyChange(() => TodayCheckOutCount);
            }
        }


        private int _guestCount;
        public int GuestCount
        {
            get => _guestCount;
            set
            {
                _guestCount = value;
                NotifyOfPropertyChange(() => GuestCount);
            }
        }


        private decimal _totalRevenue;
        public decimal TotalRevenue
        {
            get => _totalRevenue;
            set
            {
                _totalRevenue = value;
                NotifyOfPropertyChange(() => TotalRevenue);
            }
        }

        // ===== MENU STATE =====
        private string _selectedMenu;
        public string SelectedMenu
        {
            get => _selectedMenu;
            set
            {
                _selectedMenu = value;
                NotifyOfPropertyChange(() => SelectedMenu);
            }
        }

        // ===== Reservations =====
        private ObservableCollection<ReservationOverview> _reservations;
        public ObservableCollection<ReservationOverview> Reservations
        {
            get => _reservations;
            set
            {
                _reservations = value;
                NotifyOfPropertyChange(() => Reservations);
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

        private void LoadRooms()
        {
            var data = _roomRepository.GetAll();
            Rooms = new ObservableCollection<Room>(data);

            RoomCount = Rooms.Count;

            AvailableRoomCount = Rooms.Count(r => r.Status == "Verfuegbar");
            OccupiedRoomCount = Rooms.Count(r => r.Status == "Belegt");
            ReservedRoomCount = Rooms.Count(r => r.Status == "Reserviert");
        }

        private void LoadReservations()
        {
            Reservations = new ObservableCollection<ReservationOverview>(
                _reservationRepository.GetAll());

            TodayCheckInCount = Reservations.Count(r =>
                r.CheckInDate.Date == DateTime.Today);

            TodayCheckOutCount = Reservations.Count(r =>
                r.CheckOutDate.Date == DateTime.Today);

            TotalRevenue = Reservations.Sum(r => r.TotalAmount);
        }

        private void LoadGuests()
        {
            GuestCount = _gastRepository.GetAll().Count;
        }

        public void Dashboard() => SelectedMenu = "Dashboard";
        public void Zimmer() => SelectedMenu = "Zimmer";
        public void Benutzer() => SelectedMenu = "Benutzer";
        public void Dienstleistungen() => SelectedMenu = "Dienstleistungen";
        public void Events() => SelectedMenu = "Events";
        public void AlleReservierungen() => SelectedMenu = "AlleReservierungen";
    }
}
