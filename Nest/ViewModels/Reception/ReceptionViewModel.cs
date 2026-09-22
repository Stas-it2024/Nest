using Caliburn.Micro;
using Nest.ViewModels;
using Nest.ViewModels.CustomerPortal;
using Nest.ViewModels.Shell;

namespace Nest.ViewModels.Reception
{
    public class ReceptionViewModel : Conductor<Screen>
    {
        private readonly ShellViewModel _shell;
        private string _pageTitle = string.Empty;
        private string _selectedSection = string.Empty;

        public string SelectedSection
        {
            get => _selectedSection;
            private set
            {
                _selectedSection = value;
                NotifyOfPropertyChange(() => SelectedSection);
            }
        }

        public string PageTitle
        {
            get => _pageTitle;
            private set
            {
                _pageTitle = value;
                NotifyOfPropertyChange(() => PageTitle);
            }
        }

        public ReceptionViewModel(ShellViewModel shell)
        {
            _shell = shell;
            ShowReservations();
        }

        public void ShowReservations()
        {
            SelectedSection = "Reservations";
            PageTitle = "Reservierungsverwaltung";
            ActivateItemAsync(new ReservationManagementViewModel(_shell));
        }

        public void ShowGuests()
        {
            SelectedSection = "Guests";
            PageTitle = "Gästverwaltung";
            ActivateItemAsync(new GastManagementViewModel());
        }

        public void ShowAllReservations()
        {
            SelectedSection = "AllBookings";
            PageTitle = "Alle Reservierungen & Aktivitäten";
            ActivateItemAsync(new AllBookingsViewModel());
        }

        public void ShowCustomerPortal()
        {
            SelectedSection = "CustomerPortal";
            PageTitle = "Kundenportal";
            ActivateItemAsync(new CustomerPortalViewModel(_shell, null, isEmbeddedInReception: true));
        }

        public void Logout()
        {
            _shell.ShowLogin();
        }

        public void Exit()
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
