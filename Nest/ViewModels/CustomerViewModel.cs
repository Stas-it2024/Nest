using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.ViewModels
{
    public class CustomerViewModel
    {
        private readonly ShellViewModel _shell;

        public CustomerViewModel(ShellViewModel shell)
        {
            _shell = shell;
        }

        public async Task ShowLogin()
        {
            _shell.WindowTitle = "Hotel Management System - Login";
            _shell.ShowLogin();
        }
    }
}
