using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace Nest.ViewModels.CustomerPortal
{
    // Dieses ViewModel verarbeitet die Taxianfrage unabhängig von Darstellung und SQL-Code.
    public class TaxiBestellenViewModel : PropertyChangedBase
    {
        private readonly TaxiOrderRepository _taxiOrderRepository;
        private string _kundenName = string.Empty;
        private string _abholort = string.Empty;
        private string _abholzeit = string.Empty;
        private DateTime? _abholdatum;
        private Fahrzeugtyp? _ausgewaehlterFahrzeugtyp;

        public TaxiBestellenViewModel()
            : this(new TaxiOrderRepository())
        {
        }

        public TaxiBestellenViewModel(TaxiOrderRepository taxiOrderRepository)
        {
            _taxiOrderRepository = taxiOrderRepository;
            // Die Fahrzeugtypen sind feste Auswahlwerte und werden an die ComboBox gebunden.
            Fahrzeugtypen = new ObservableCollection<Fahrzeugtyp>
            {
                new Fahrzeugtyp { Bezeichnung = "Standard (4 Pers.)" },
                new Fahrzeugtyp { Bezeichnung = "Kombi (6 Pers.)" },
                new Fahrzeugtyp { Bezeichnung = "Bus (8 Pers.)" }
            };

            AusgewaehlterFahrzeugtyp = Fahrzeugtypen[0];
            // Ein realistischer Vorschlag in der Zukunft erleichtert die Eingabe und verhindert Fehlstarts.
            var vorgeschlageneAbholung = DateTime.Now.AddHours(2);
            Abholdatum = vorgeschlageneAbholung.Date;
            Abholzeit = vorgeschlageneAbholung.ToString("HH:mm");
        }

        public string KundenName
        {
            get => _kundenName;
            set
            {
                _kundenName = value;
                NotifyOfPropertyChange(() => KundenName);
            }
        }

        public string Abholort
        {
            get => _abholort;
            set
            {
                _abholort = value;
                NotifyOfPropertyChange(() => Abholort);
            }
        }

        public DateTime? Abholdatum
        {
            get => _abholdatum;
            set
            {
                _abholdatum = value?.Date;
                NotifyOfPropertyChange(() => Abholdatum);
            }
        }

        public DateTime FruehestesAbholdatum => DateTime.Today;

        public string Abholzeit
        {
            get => _abholzeit;
            set
            {
                _abholzeit = value;
                NotifyOfPropertyChange(() => Abholzeit);
            }
        }

        public ObservableCollection<Fahrzeugtyp> Fahrzeugtypen { get; }

        public Fahrzeugtyp? AusgewaehlterFahrzeugtyp
        {
            get => _ausgewaehlterFahrzeugtyp;
            set
            {
                _ausgewaehlterFahrzeugtyp = value;
                NotifyOfPropertyChange(() => AusgewaehlterFahrzeugtyp);
            }
        }

        private List<string> ErmittleValidierungsfehler()
        {
            // TryParse prüft Benutzereingaben ohne eine Ausnahme bei ungültigem Format auszulösen.
            var fehler = new List<string>();

            if (string.IsNullOrWhiteSpace(KundenName))
            {
                fehler.Add("Name eingeben");
            }

            if (string.IsNullOrWhiteSpace(Abholort))
            {
                fehler.Add("Abholort eingeben");
            }

            if (Abholdatum == null)
            {
                fehler.Add("Gültiges Abholdatum auswählen");
            }
            else if (Abholdatum.Value.Date < DateTime.Today)
            {
                fehler.Add("Das Taxidatum darf nicht in der Vergangenheit liegen.");
            }

            if (!TimeOnly.TryParse(Abholzeit, out var abholzeit))
            {
                fehler.Add("Gültige Abholzeit eingeben");
            }
            else if (Abholdatum?.Date == DateTime.Today
                && abholzeit <= TimeOnly.FromDateTime(DateTime.Now))
            {
                fehler.Add("Die Abholzeit darf nicht in der Vergangenheit liegen.");
            }

            if (AusgewaehlterFahrzeugtyp == null)
            {
                fehler.Add("Fahrzeugtyp auswählen");
            }

            return fehler;
        }

        private static void ZeigeValidierungsfehler(IReadOnlyCollection<string> fehler)
        {
            MessageBox.Show(
                "Bitte überprüfen Sie folgende Angaben:\n\n- " + string.Join("\n- ", fehler),
                "Unvollständige oder ungültige Angaben",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        public void TaxiBestellen()
        {
            // Erst nach vollständiger Validierung wird aus den Eingaben ein Datenmodell erzeugt.
            var validierungsfehler = ErmittleValidierungsfehler();
            if (validierungsfehler.Count > 0)
            {
                ZeigeValidierungsfehler(validierungsfehler);
                return;
            }

            if (!TimeOnly.TryParse(Abholzeit, out var abholzeit))
            {
                ZeigeValidierungsfehler(new[] { "Gültige Abholzeit eingeben" });
                return;
            }

            var fahrzeugtyp = AusgewaehlterFahrzeugtyp!;
            var bestellung = new TaxiOrder
            {
                // Taxi- und Zimmerreservierung können im aktuellen Ablauf unabhängig sein.
                ReservationId = null,
                GuestName = KundenName.Trim(),
                // Die aktuelle Taxioberfläche besitzt kein Telefonfeld; die Datenbankspalte erlaubt NULL.
                GuestPhone = null,
                PickupLocation = Abholort.Trim(),
                PickupDate = Abholdatum!.Value,
                PickupTime = abholzeit.ToTimeSpan(),
                VehicleType = fahrzeugtyp.Bezeichnung,
                Status = "Angefragt"
            };

            try
            {
                // Das Repository führt den einzelnen parametrisierten INSERT aus.
                _taxiOrderRepository.Add(bestellung);
                MessageBox.Show(
                    $"Ihre Taxianfrage für {KundenName} ab „{Abholort}“ wurde für den {Abholdatum:dd.MM.yyyy} um {Abholzeit} Uhr erfolgreich erfasst.\n"
                    + $"Fahrzeugtyp: {fahrzeugtyp.Bezeichnung}",
                    "Taxianfrage erfasst",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Die Taxianfrage konnte nicht in der Datenbank gespeichert werden. Bitte versuchen Sie es erneut.\n\n{ex.Message}",
                    "Datenbankfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
