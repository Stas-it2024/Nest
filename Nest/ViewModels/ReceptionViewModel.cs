using Caliburn.Micro;

namespace Nest.ViewModels
{
    public class ReceptionViewModel : Screen
    {
        private readonly ShellViewModel _shell;

        public ReceptionViewModel(ShellViewModel shell)
        {
            _shell = shell;
        }

        public void ShowReservations()
        {
            // Hier später Reservierungs-View laden
        }

        public void ShowGuests()
        {
            // Hier später Gäste-View laden
        }

        public void ShowAllReservations()
        {
            // Hier später Alle-Buchungen-View laden
        }

        public void ShowCustomerPortal()
        {
            // Hier später Kundenportal-View laden
        }

        public void ShowReception()
        {
            _shell.WindowTitle = "Hotel Management System - Reception";
            _shell.ShowReception();
        }

        /*public void Exit()
        {
            Application.Current.Shutdown();
        }*/
    }
}