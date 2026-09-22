using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace Nest.ViewModels.CustomerPortal
{
    // Das ViewModel enthält Eingabedaten, Validierung und Preislogik; die View bleibt dadurch schlank.
    public class AutoMietenViewModel : PropertyChangedBase
    {
        // Das Repository übernimmt ausschließlich das Speichern in MySQL.
        private readonly AutoRentalRepository _autoRentalRepository;
        private string _fahrerName = string.Empty;
        private string _telefon = string.Empty;
        private string _preisInfo = "Preis: -- €";
        private Fahrzeugkategorie? _ausgewaehlteKategorie;
        private DateTime? _abholdatum;
        private DateTime? _rueckgabedatum;

        public AutoMietenViewModel()
            : this(new AutoRentalRepository())
        {
        }

        public AutoMietenViewModel(AutoRentalRepository autoRentalRepository)
        {
            _autoRentalRepository = autoRentalRepository;
            // Die Kategorien sind im aktuellen Projekt feste Konfigurationswerte.
            // ObservableCollection eignet sich für die direkte Bindung an die ComboBox.
            Kategorien = new ObservableCollection<Fahrzeugkategorie>
            {
                new Fahrzeugkategorie { Name = "Kleinwagen", Tagespreis = 29.99m },
                new Fahrzeugkategorie { Name = "Mittelklasse", Tagespreis = 49.99m },
                new Fahrzeugkategorie { Name = "SUV", Tagespreis = 69.99m }
            };

            AusgewaehlteKategorie = Kategorien[0];
            Abholdatum = DateTime.Today.AddDays(1);
            Rueckgabedatum = DateTime.Today.AddDays(3);
        }

        public string FahrerName
        {
            get => _fahrerName;
            set
            {
                _fahrerName = value;
                // Informiert die gebundene View über die geänderte Eigenschaft.
                NotifyOfPropertyChange(() => FahrerName);
            }
        }

        public string Telefon
        {
            get => _telefon;
            set
            {
                _telefon = value;
                NotifyOfPropertyChange(() => Telefon);
            }
        }

        public ObservableCollection<Fahrzeugkategorie> Kategorien { get; }

        public Fahrzeugkategorie? AusgewaehlteKategorie
        {
            get => _ausgewaehlteKategorie;
            set
            {
                _ausgewaehlteKategorie = value;
                NotifyOfPropertyChange(() => AusgewaehlteKategorie);
                BerechnePreis();
            }
        }

        public DateTime? Abholdatum
        {
            get => _abholdatum;
            set
            {
                _abholdatum = value?.Date;
                NotifyOfPropertyChange(() => Abholdatum);
                NotifyOfPropertyChange(() => FruehestesRueckgabedatum);
                BerechnePreis();
            }
        }

        public DateTime? Rueckgabedatum
        {
            get => _rueckgabedatum;
            set
            {
                _rueckgabedatum = value?.Date;
                NotifyOfPropertyChange(() => Rueckgabedatum);
                BerechnePreis();
            }
        }

        public DateTime FruehestesAbholdatum => DateTime.Today;

        public DateTime FruehestesRueckgabedatum => Abholdatum?.Date.AddDays(1) ?? DateTime.Today.AddDays(1);

        public string PreisInfo
        {
            get => _preisInfo;
            private set
            {
                _preisInfo = value;
                NotifyOfPropertyChange(() => PreisInfo);
            }
        }

        private void BerechnePreis()
        {
            // Ohne vollständige Auswahl ist keine verlässliche Preisberechnung möglich.
            if (AusgewaehlteKategorie == null || Abholdatum == null || Rueckgabedatum == null)
            {
                PreisInfo = "Preis: -- €";
                return;
            }

            var tage = (Rueckgabedatum.Value - Abholdatum.Value).Days;
            if (tage <= 0)
            {
                PreisInfo = "Bitte wählen Sie einen gültigen Mietzeitraum.";
                return;
            }

            var zeiteinheit = tage == 1 ? "Tag" : "Tage";
            // decimal wird für Geld verwendet, um typische Rundungsfehler von double zu vermeiden.
            PreisInfo = $"Gesamtpreis für {tage} {zeiteinheit}: {tage * AusgewaehlteKategorie.Tagespreis:F2} €";
        }

        private List<string> ErmittleValidierungsfehler()
        {
            // Alle Fehler werden gesammelt, damit der Benutzer sie gemeinsam korrigieren kann.
            var fehler = new List<string>();

            if (string.IsNullOrWhiteSpace(FahrerName))
            {
                fehler.Add("Name des Fahrers eingeben");
            }

            if (string.IsNullOrWhiteSpace(Telefon))
            {
                fehler.Add("Telefonnummer eingeben");
            }

            if (AusgewaehlteKategorie == null)
            {
                fehler.Add("Fahrzeugkategorie auswählen");
            }

            if (Abholdatum == null)
            {
                fehler.Add("Gültiges Abholdatum auswählen");
            }
            else if (Abholdatum.Value.Date < DateTime.Today)
            {
                fehler.Add("Der Mietbeginn darf nicht in der Vergangenheit liegen.");
            }

            if (Rueckgabedatum == null)
            {
                fehler.Add("Gültiges Rückgabedatum auswählen");
            }
            else if (Abholdatum != null && Rueckgabedatum.Value.Date <= Abholdatum.Value.Date)
            {
                fehler.Add("Das Mietende muss nach dem Mietbeginn liegen.");
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

        public void AutoMieten()
        {
            // Ungültige Daten dürfen das Repository und damit die Datenbank nicht erreichen.
            var validierungsfehler = ErmittleValidierungsfehler();
            if (validierungsfehler.Count > 0)
            {
                ZeigeValidierungsfehler(validierungsfehler);
                return;
            }

            // Nach erfolgreicher Validierung sind diese nullable Werte sicher gesetzt;
            // der Operator ! unterdrückt hier nur die Compilerwarnung und prüft nicht zur Laufzeit.
            var kategorie = AusgewaehlteKategorie!;
            var abholdatum = Abholdatum!.Value;
            var rueckgabedatum = Rueckgabedatum!.Value;
            var miettage = (rueckgabedatum - abholdatum).Days;
            var gesamtpreis = miettage * kategorie.Tagespreis;

            var miete = new AutoRental
            {
                // Die Mietanfrage darf unabhängig von einer Zimmerreservierung angelegt werden.
                ReservationId = null,
                DriverName = FahrerName.Trim(),
                Phone = Telefon.Trim(),
                VehicleCategory = kategorie.Name,
                PickupDate = abholdatum,
                ReturnDate = rueckgabedatum,
                DailyPrice = kategorie.Tagespreis,
                TotalPrice = gesamtpreis,
                Status = "Angefragt"
            };

            try
            {
                // Ein einzelner INSERT ist atomar und benötigt hier keine zusätzliche Transaktion.
                _autoRentalRepository.Add(miete);
                MessageBox.Show(
                    $"Ihre Mietanfrage für die Kategorie „{kategorie.Name}“ wurde erfolgreich erfasst.\n{PreisInfo}",
                    "Mietanfrage erfasst",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Die Mietanfrage konnte nicht in der Datenbank gespeichert werden. Bitte versuchen Sie es erneut.\n\n{ex.Message}",
                    "Datenbankfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
