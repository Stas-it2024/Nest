using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace Nest.ViewModels.Reception
{
    /// <summary>
    /// Verwaltet die Gästeliste und den Aufenthaltsverlauf in der Rezeption.
    /// </summary>
    public class GastManagementViewModel : Screen
    {
        private readonly GastRepository _gastRepository;
        private string _searchText = "";
        private ObservableCollection<Guest> _guests;
        private Guest _selectedGuest;
        private ObservableCollection<ReservationOverview> _guestHistory;

        public GastManagementViewModel()
        {
            _gastRepository = new GastRepository();
            LoadGuests();
        }

        public ObservableCollection<Guest> Guests
        {
            get => _guests;
            set
            {
                _guests = value;
                NotifyOfPropertyChange(() => Guests);
            }
        }

        public ObservableCollection<ReservationOverview> GuestHistory
        {
            get => _guestHistory;
            set
            {
                _guestHistory = value;
                NotifyOfPropertyChange(() => GuestHistory);
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                NotifyOfPropertyChange(() => SearchText);
                SearchGuests();
            }
        }

        public Guest SelectedGuest
        {
            get => _selectedGuest;
            set
            {
                _selectedGuest = value;
                NotifyOfPropertyChange(() => SelectedGuest);
                LoadGuestHistory();
            }
        }

        public void LoadGuests()
        {
            try
            {
                Guests = new ObservableCollection<Guest>(_gastRepository.GetAll());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Laden der Gäste: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SearchGuests()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                LoadGuests();
                return;
            }

            try
            {
                var filtered = _gastRepository.GetAll().FindAll(g =>
                    (g.FirstName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (g.LastName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (g.Email?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (g.Phone?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

                Guests = new ObservableCollection<Guest>(filtered);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler bei der Suche: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadGuestHistory()
        {
            if (SelectedGuest == null)
            {
                GuestHistory = new ObservableCollection<ReservationOverview>();
                return;
            }

            try
            {
                var history = new ReservationRepository().GetByGuestId(SelectedGuest.Id);
                GuestHistory = new ObservableCollection<ReservationOverview>(history);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Laden des Verlaufs: {ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ClearSearch()
        {
            SearchText = "";
            LoadGuests();
        }

        public new void Refresh()
        {
            LoadGuests();
            GuestHistory = new ObservableCollection<ReservationOverview>();
            SelectedGuest = null!;
        }
    }
}
