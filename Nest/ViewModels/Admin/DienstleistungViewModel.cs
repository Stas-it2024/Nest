using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Nest.ViewModels.Admin
{
    public class DienstleistungViewModel : Screen
    {
        // Repository für Dienstleistungen
        private readonly ServiceRepository _serviceRepository;

        private string _serviceName;
        private string _category;
        private decimal _price;
        private string _status;
        private Service _selectedService;

        // ObservableCollection für die Dienstleistungen
        public ObservableCollection<Service> Services { get; set; }

        // Eigenschaften für die Eingabefelder
        public string ServiceName
        {
            get => _serviceName;
            set
            {
                _serviceName = value;
                NotifyOfPropertyChange(() => ServiceName);
            }
        }

        // Eigenschaften für die Eingabefelder
        public string Category
        {
            get => _category;
            set
            {
                _category = value;
                NotifyOfPropertyChange(() => Category);
                NotifyOfPropertyChange(() => SelectedCategoryIndex);
            }
        }

        // Eigenschaften für die Eingabefelder
        public string CategoryDisplay
        {
            get
            {
                return Category switch
                {
                    "Speisen und Getraenke" => "Speisen und Getränke",
                    "Waesche" => "Wäsche",
                    "Wellness" => "Wellness",
                    "Transport" => "Transport",
                    "Sonstiges" => "Sonstiges",
                    _ => Category
                };
            }
        }

        // Eigenschaften für die Eingabefelder
        public decimal Price
        {
            get => _price;
            set
            {
                _price = value;
                NotifyOfPropertyChange(() => Price);
            }
        }

        // Eigenschaften für die Eingabefelder
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                NotifyOfPropertyChange(() => Status);
                NotifyOfPropertyChange(() => SelectedStatusIndex);
            }
        }

        // Eigenschaften für die Eingabefelder
        public Service SelectedService
        {
            get => _selectedService;
            set
            {
                _selectedService = value;
                NotifyOfPropertyChange(() => SelectedService);

                if (value != null)
                {
                    ServiceName = value.ServiceName;
                    Category = value.Category;
                    Price = value.Price;
                    Status = value.Status;
                }
            }
        }

        public int SelectedCategoryIndex
        {
            get
            {
                return Category switch
                {
                    "Speisen und Getraenke" => 0,
                    "Waesche" => 1,
                    "Wellness" => 2,
                    "Transport" => 3,
                    "Sonstiges" => 4,
                    _ => 4
                };
            }
            set
            {
                Category = value switch
                {
                    0 => "Speisen und Getraenke",
                    1 => "Waesche",
                    2 => "Wellness",
                    3 => "Transport",
                    4 => "Sonstiges",
                    _ => "Sonstiges"
                };

                NotifyOfPropertyChange(() => SelectedCategoryIndex);
            }
        }

        public int SelectedStatusIndex
        {
            get
            {
                return Status switch
                {
                    "Verfuegbar" => 0,
                    "Nicht_verfuegbar" => 1,
                    _ => 0
                };
            }
            set
            {
                Status = value switch
                {
                    0 => "Verfuegbar",
                    1 => "Nicht_verfuegbar",
                    _ => "Verfuegbar"
                };

                NotifyOfPropertyChange(() => SelectedStatusIndex);
            }
        }

        // Konstruktor
        public DienstleistungViewModel()
        {
            _serviceRepository = new ServiceRepository();

            Services = new ObservableCollection<Service>(
                _serviceRepository.GetAll()
            );

            ClearForm();
        }

        // Methoden für die Buttons
        public void Hinzufügen()
        {
            if (string.IsNullOrWhiteSpace(ServiceName))
            {
                MessageBox.Show(
                    "Bitte geben Sie einen Namen ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            var existingService = Services.FirstOrDefault(s =>
    s.ServiceName.Equals(ServiceName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (existingService != null)
            {
                MessageBox.Show(
                    "Eine Dienstleistung mit diesem Namen existiert bereits.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (Price <= 0)
            {
                MessageBox.Show(
                    "Bitte geben Sie einen gültigen Preis ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            var service = new Service
            {
                ServiceName = ServiceName,
                Category = Category,
                Price = Price,
                Status = Status
            };

            bool success = _serviceRepository.Add(service);

            if (!success)
            {
                MessageBox.Show(
                    "Die Dienstleistung konnte nicht hinzugefügt werden.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            Services.Add(service);

            MessageBox.Show(
                "Dienstleistung erfolgreich hinzugefügt!",
                "Erfolg",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        public void Aktualisieren()
        {
            if (SelectedService == null)
            {
                MessageBox.Show(
                    "Bitte wählen Sie eine Dienstleistung aus.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            SelectedService.ServiceName = ServiceName;
            SelectedService.Category = Category;
            SelectedService.Price = Price;
            SelectedService.Status = Status;

            int serviceId = SelectedService.Id;

            _serviceRepository.Update(SelectedService);

            Services = new ObservableCollection<Service>(
                _serviceRepository.GetAll()
            );

            SelectedService = Services.FirstOrDefault(s => s.Id == serviceId);

            NotifyOfPropertyChange(() => Services);

            MessageBox.Show(
                "Dienstleistung erfolgreich aktualisiert!",
                "Erfolg",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        public void Löschen()
        {
            if (SelectedService == null)
            {
                MessageBox.Show(
                    "Bitte wählen Sie eine Dienstleistung aus.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            var result = MessageBox.Show(
                "Möchten Sie diese Dienstleistung wirklich löschen?",
                "Bestätigung",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            _serviceRepository.Delete(SelectedService.Id);

            Services = new ObservableCollection<Service>(
                _serviceRepository.GetAll()
            );

            SelectedService = null;

            NotifyOfPropertyChange(() => Services);

            MessageBox.Show(
                "Dienstleistung erfolgreich gelöscht!",
                "Erfolg",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }


        public void ClearForm()
        {
            ServiceName = "";
            Category = "Speisen und Getraenke";
            Price = 0;
            Status = "Verfuegbar";

            SelectedService = null;
        }

        private string _priceText = "0,00";

        public string PriceText
        {
            get => _priceText;
            set
            {
                _priceText = value;
                NotifyOfPropertyChange(() => PriceText);
                NotifyOfPropertyChange(() => CanPreisUp);
                NotifyOfPropertyChange(() => CanPreisDown);
            }
        }

        public void PreisUp()
        {
            decimal preis = GetPrice();

            if (preis < 9999.99m)
            {
                preis += 1.00m;

                if (preis > 9999.99m)
                    preis = 9999.99m;

                PriceText = preis.ToString(
                    "F2",
                    CultureInfo.GetCultureInfo("de-DE"));
            }
        }

        public void PreisDown()
        {
            decimal preis = GetPrice();

            if (preis > 0)
            {
                preis -= 1.00m;

                if (preis < 0)
                    preis = 0;

                PriceText = preis.ToString(
                    "F2",
                    CultureInfo.GetCultureInfo("de-DE"));
            }
        }

        public void FormatPrice()
        {
            decimal preis = GetPrice();

            if (preis < 0)
                preis = 0;

            if (preis > 9999.99m)
                preis = 9999.99m;

            PriceText = preis.ToString(
                "F2",
                CultureInfo.GetCultureInfo("de-DE"));
        }

        public bool CanPreisUp
        {
            get
            {
                return GetPrice() < 9999.99m;
            }
        }

        public bool CanPreisDown
        {
            get
            {
                return GetPrice() > 0;
            }
        }

        private decimal GetPrice()
        {
            if (decimal.TryParse(
                PriceText,
                NumberStyles.Number,
                CultureInfo.GetCultureInfo("de-DE"),
                out decimal preis))
            {
                return preis;
            }

            return 0;
        }
    }
}
