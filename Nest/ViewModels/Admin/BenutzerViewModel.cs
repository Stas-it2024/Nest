using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Nest.ViewModels.Admin
{
    internal class BenutzerViewModel : Screen
    {
        // Repository für Benutzer
        private readonly UserRepository _userRepository;

        public ObservableCollection<User> Users { get; set; }

        public BenutzerViewModel()
        {
            _userRepository = new UserRepository();

            Users = new ObservableCollection<User>(
                _userRepository.GetAll()
            );
        }

        // Eigenschaft für den ausgewählten Benutzer
        private User _selectedUser;

        public User SelectedUser
        {
            get => _selectedUser;
            set
            {
                _selectedUser = value;

                if (_selectedUser != null)
                {
                    UserName = _selectedUser.UserName;
                    FullName = _selectedUser.FullName;
                    Role = _selectedUser.Role;
                    Status = _selectedUser.Status;
                }

                NotifyOfPropertyChange(() => SelectedUser);
            }
        }

        // Eigenschaften für die Eingabefelder
        private string _userName;
        private string _fullName;
        private string _password;
        private string _role;
        private string _status;

        public string UserName
        {
            get => _userName;
            set
            {
                _userName = value;
                NotifyOfPropertyChange(() => UserName);
            }
        }

        public string FullName
        {
            get => _fullName;
            set
            {
                _fullName = value;
                NotifyOfPropertyChange(() => FullName);
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                NotifyOfPropertyChange(() => Password);
            }
        }

        public string Role
        {
            get => _role;
            set
            {
                _role = value;
                NotifyOfPropertyChange(() => Role);
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                NotifyOfPropertyChange(() => Status);
            }
        }

        // Methode zum Hinzufügen eines neuen Benutzers
        public void Hinzufügen()
        {
            if (string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show(
                    "Bitte geben Sie ein Passwort ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }
            var user = new User
            {
                UserName = UserName,
                FullName = FullName,
                Password = Password,
                Role = Role,
                Status = Status,
                DateAdded = DateTime.Now
            };

            _userRepository.Add(user);

            Users.Add(user);

            MessageBox.Show(
                "Benutzer erfolgreich hinzugefügt!",
                "Erfolg",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // Methode zum Aktualisieren eines Benutzers
        public void ClearForm()
        {
            UserName = "";
            FullName = "";
            Password = "";
            Role = "";
            Status = "";
        }

        // Methode zum Aktualisieren eines Benutzers
        public void Aktualisieren()
        {
            if (SelectedUser == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Benutzer aus.");
                return;
            }

            SelectedUser.UserName = UserName;
            SelectedUser.FullName = FullName;
            SelectedUser.Password = Password;
            SelectedUser.Role = Role;
            SelectedUser.Status = Status;

            int updatedUserId = SelectedUser.Id;

            _userRepository.Update(SelectedUser);

            Users = new ObservableCollection<User>(
                _userRepository.GetAll()
            );

            SelectedUser = Users.FirstOrDefault(u => u.Id == updatedUserId);

            NotifyOfPropertyChange(() => Users);

            MessageBox.Show(
                "Benutzer erfolgreich aktualisiert!",
                "Erfolg",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // Methode zum Löschen eines Benutzers
        public void Löschen()
        {
            if (SelectedUser == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Benutzer aus.");
                return;
            }

            var result = MessageBox.Show(
                "Möchten Sie diesen Benutzer wirklich löschen?",
                "Bestätigung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _userRepository.Delete(SelectedUser.Id);

            Users = new ObservableCollection<User>(
                _userRepository.GetAll()
            );

            SelectedUser = null;

            NotifyOfPropertyChange(() => Users);
        }
    }
}