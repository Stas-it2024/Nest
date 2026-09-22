using Caliburn.Micro;
using Nest.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Media3D;
using Nest.ViewModels.Admin;
using Nest.ViewModels.Reception;
using Nest.ViewModels.CustomerPortal;

using Nest.ViewModels.Authentication;

namespace Nest.ViewModels.Shell
{
    public class ShellViewModel : Conductor<object>
    {
        private string _windowTitle;

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
            var window = Application.Current.MainWindow;

            window.WindowState = WindowState.Normal;
            window.Width = 820;
            window.Height = 600;
            window.ResizeMode = ResizeMode.CanMinimize;

            ActivateItemAsync(new LoginViewModel(this));
        }

        // für Reception hinzufügen
        public void ShowReception()
        {
            var window = Application.Current.MainWindow;

            WindowTitle = "Hotel Management System - Reception";
            window.WindowState = WindowState.Maximized;
            window.ResizeMode = ResizeMode.CanResize;

            ActivateItemAsync(new ReceptionViewModel(this));
        }

        public void ShowCustomerPortal(string? gastname)
        {
            var window = Application.Current.MainWindow;

            window.WindowState = WindowState.Maximized;
            window.ResizeMode = ResizeMode.CanResize;

            ActivateItemAsync(new CustomerPortalViewModel(this, gastname));
        }

        internal void ShowAdmin()
        {
            var window = Application.Current.MainWindow;

            window.WindowState = WindowState.Maximized;
            window.ResizeMode = ResizeMode.CanResize;

            ActivateItemAsync(new AdminViewModel(this));
        }

    }
}
