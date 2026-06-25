using Caliburn.Micro;
using Nest.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.ViewModels
{
    public class ShellViewModel : Conductor<object>
    {
        private string _windowTitle = "Hotel Management System";

        public string WindowTitle
        {
            get => _windowTitle;
            set
            {
                _windowTitle = value;
                NotifyOfPropertyChange(() => WindowTitle);
            }
        }
        public ShellViewModel()
        {
            WindowTitle = "Hotel Management System - Login";
            _ = ActivateItemAsync(new LoginViewModel(this));
        }

        public void ShowCustomer()
        {
            ActivateItemAsync(new CustomerViewModel(this));
        }

        public void ShowLogin()
        {
            ActivateItemAsync(new LoginViewModel(this));
        }

        // für Reception hinzufügen
        public void ShowReception()
        {
            ActivateItemAsync(new ReceptionViewModel(this));
        }

        public void ShowCustomerPortal()
        {
            ActivateItemAsync(new CustomerPortalViewModel(this));
        }

        internal void ShowAdmin()
        {
            ActivateItemAsync(new AdminViewModel(this));
        }
    }
}
