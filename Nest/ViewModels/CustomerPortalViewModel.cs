using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Nest.ViewModels
{
    /// <summary>
    /// Haupt-ViewModel für das Kundenportal – schlanke Version
    /// </summary>
    public class CustomerPortalViewModel
    {
        private readonly ShellViewModel _shell;

        public CustomerPortalViewModel(ShellViewModel shell)
        {
            _shell = shell;

        }

        public void ShowCustomerPortal()
        {
            _shell.WindowTitle = "Hotel Management System - Kundenportal";
            _shell.ShowCustomerPortal();
        }
    }
}