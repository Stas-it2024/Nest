using Nest.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using System.Windows.Controls;

using Nest.ViewModels.Shell;

namespace Nest.ViewModels.Authentication
{
    public class LoginViewModel : Screen
    {
        private readonly ShellViewModel _shell;
        private readonly UserRepository _userRepository;
        private string _password;
        private bool _showPassword;

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                NotifyOfPropertyChange(() => Password);
            }
        }
        
        public bool ShowPassword
        {
            get => _showPassword;
            set
            {
                _showPassword = value;
                NotifyOfPropertyChange(() => ShowPassword);
                NotifyOfPropertyChange(() => PasswordBoxVisibility);
                NotifyOfPropertyChange(() => TextBoxVisibility);
            }
        }

        public System.Windows.Visibility PasswordBoxVisibility =>
            ShowPassword ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        public System.Windows.Visibility TextBoxVisibility =>
            ShowPassword ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public string UserName { get; set; }

        public LoginViewModel(ShellViewModel shell)
        {
            _shell = shell;
            _userRepository = new UserRepository();
        }


        public void ShowCustomer()
        {
            _shell.WindowTitle = "Hotel Management System - Kundenportal";
            _shell.ShowCustomer();
        }

        // Neue Login-Methode
        public void Login()
        {
            var user = _userRepository.Login(UserName, Password);

            if (user == null)
            {
                System.Windows.MessageBox.Show("Wrong login or passwort");
                return;
            }

            if (user.Role == "Rezeptionist")
            {
                _shell.WindowTitle = "Hotel Management System - Reception";
                _shell.ShowReception();
            }
            else if (user.Role == "Admin")
            {
                _shell.WindowTitle = "Hotel Management System - Admin";
                _shell.ShowAdmin();
            }
        }

        public void SetPassword(object source)
        {
            var box = (PasswordBox)source;
            Password = box.Password;
        }
    }
}
