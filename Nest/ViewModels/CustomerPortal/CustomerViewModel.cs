using Caliburn.Micro;

using Nest.ViewModels.Shell;

namespace Nest.ViewModels.CustomerPortal
{
    // Dieses ViewModel hält die Startseite für Kunden frei von Navigationslogik im Code-Behind.
    public class CustomerViewModel : Screen
    {
        private readonly ShellViewModel _shell;
        private string _gastname = string.Empty;

        public CustomerViewModel(ShellViewModel shell) => _shell = shell;

        public string Gastname
        {
            get => _gastname;
            set
            {
                _gastname = value ?? string.Empty;
                // Die View wird sofort aktualisiert, weil das Textfeld per Data Binding verbunden ist.
                NotifyOfPropertyChange(() => Gastname);
            }
        }

        public void ShowCustomerPortal()
        {
            // Der Name wird an das Portal übergeben, damit die Begrüßung personalisiert werden kann.
            _shell.WindowTitle = "Hotel Management System - Kundenportal";
            _shell.ShowCustomerPortal(Gastname);
        }

        public void ShowLogin()
        {
            // Die Shell wechselt die aktive Hauptansicht; die View navigiert nicht selbst.
            _shell.WindowTitle = "Hotel Management System - Login";
            _shell.ShowLogin();
        }
    }
}
