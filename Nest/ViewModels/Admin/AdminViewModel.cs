using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using Nest.ViewModels.Reception;
using Nest.ViewModels.Shell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Formats.Asn1;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.ViewModels.Admin
{
    public class AdminViewModel : Conductor<Screen>
    {
        private readonly ShellViewModel _shell;
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

        public AdminViewModel(ShellViewModel shell)
        {
            _shell = shell;

            ShowDashboard();
        }

        public void ShowDashboard()
        {
            SelectedSection = "Dashboard";
            ActivateItemAsync(new DashboardViewModel());
        }

        public void ShowZimmer()
        {
            SelectedSection = "Zimmer";
            ActivateItemAsync(new ZimmerViewModel());
        }

        public void ShowBenutzer()
        {
            SelectedSection = "Benutzer";
            ActivateItemAsync(new BenutzerViewModel());
        }

        public void ShowDienstleistungen()
        {
            SelectedSection = "Dienstleistungen";
            ActivateItemAsync(new DienstleistungViewModel());
        }

        public void ShowEvents()
        {
            SelectedSection = "Events";
            ActivateItemAsync(new EventViewModel());
        }

        public void ShowAlleReservierungen()
        {
            SelectedSection = "AlleReservierungen";
            ActivateItemAsync(new AllBookingsViewModel(showPageHeader: true));
        }

        public void Abmelden()
        {
            _shell.ShowLogin();
        }

        public void Beenden()
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
