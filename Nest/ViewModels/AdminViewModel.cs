using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caliburn.Micro;

namespace Nest.ViewModels
{
    public class AdminViewModel : Screen
    {
        private readonly ShellViewModel _shell;
        public AdminViewModel(ShellViewModel shell)
        {
            _shell = shell;
        }
        public void ShowAdmin()
        {
            _shell.WindowTitle = "Hotel Management System - Admin";
            _shell.ShowAdmin();
        }
    }
}
