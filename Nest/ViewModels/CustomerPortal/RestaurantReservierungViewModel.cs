using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace Nest.ViewModels.CustomerPortal
{
    // Das ViewModel bündelt Eingaben, Regeln und Speicherablauf der Tischreservierung.
    public class RestaurantReservierungViewModel : PropertyChangedBase
    {
        // Eine zentrale Konstante verhindert unterschiedliche Höchstwerte in Anzeige und Validierung.
        private const int MaximaleGaestezahl = 8;

        private readonly RestaurantReservationRepository _restaurantReservationRepository;
        private string _kundenName = string.Empty;
        private string _reservierungszeit = string.Empty;
        private string _anzahlGaeste = string.Empty;
        private string _sonderwuensche = string.Empty;
        private DateTime? _reservierungsdatum;
        private Anlass? _ausgewaehlterAnlass;

        public RestaurantReservierungViewModel()
            : this(new RestaurantReservationRepository())
        {
        }

        public RestaurantReservierungViewModel(
            RestaurantReservationRepository restaurantReservationRepository)
        {
            _restaurantReservationRepository = restaurantReservationRepository;
            // Die Anlässe sind bewusst feste Auswahlwerte für eine einheitliche Eingabe.
            Anlaesse = new ObservableCollection<Anlass>
            {
                new Anlass { Bezeichnung = "Privat" },
                new Anlass { Bezeichnung = "Geschäftlich" },
                new Anlass { Bezeichnung = "Feier" }
            };

            AusgewaehlterAnlass = Anlaesse[0];
            Reservierungsdatum = DateTime.Today.AddDays(2);
            Reservierungszeit = "19:00";
            AnzahlGaeste = "2";
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

        public DateTime? Reservierungsdatum
        {
            get => _reservierungsdatum;
            set
            {
                _reservierungsdatum = value;
                NotifyOfPropertyChange(() => Reservierungsdatum);
            }
        }

        public string Reservierungszeit
        {
            get => _reservierungszeit;
            set
            {
                _reservierungszeit = value;
                NotifyOfPropertyChange(() => Reservierungszeit);
            }
        }

        public string AnzahlGaeste
        {
            get => _anzahlGaeste;
            set
            {
                _anzahlGaeste = value;
                NotifyOfPropertyChange(() => AnzahlGaeste);
                NotifyOfPropertyChange(() => AnzahlGaesteHinweis);
            }
        }

        public string AnzahlGaesteHinweis
        {
            get
            {
                // Der Hinweis reagiert sofort auf die Eingabe, ohne bereits Daten zu speichern.
                if (string.IsNullOrWhiteSpace(AnzahlGaeste))
                {
                    return string.Empty;
                }

                if (!int.TryParse(AnzahlGaeste, out var anzahlGaeste))
                {
                    return "Die Gästezahl muss eine ganze Zahl sein.";
                }

                if (anzahlGaeste < 1 || anzahlGaeste > MaximaleGaestezahl)
                {
                    return $"Die Gästezahl muss zwischen 1 und {MaximaleGaestezahl} liegen.";
                }

                return string.Empty;
            }
        }

        public ObservableCollection<Anlass> Anlaesse { get; }

        public Anlass? AusgewaehlterAnlass
        {
            get => _ausgewaehlterAnlass;
            set
            {
                _ausgewaehlterAnlass = value;
                NotifyOfPropertyChange(() => AusgewaehlterAnlass);
            }
        }

        public string Sonderwuensche
        {
            get => _sonderwuensche;
            set
            {
                _sonderwuensche = value;
                NotifyOfPropertyChange(() => Sonderwuensche);
            }
        }

        private List<string> ErmittleValidierungsfehler()
        {
            // Die Fachregel begrenzt eine Tischreservierung auf eine ganze Zahl von 1 bis 8 Personen.
            var fehler = new List<string>();

            if (string.IsNullOrWhiteSpace(KundenName))
            {
                fehler.Add("Name eingeben");
            }

            if (Reservierungsdatum == null)
            {
                fehler.Add("Gültiges Reservierungsdatum auswählen");
            }

            if (!TimeOnly.TryParse(Reservierungszeit, out _))
            {
                fehler.Add("Gültige Reservierungszeit eingeben");
            }

            if (!int.TryParse(AnzahlGaeste, out var anzahlGaeste)
                || anzahlGaeste < 1
                || anzahlGaeste > MaximaleGaestezahl)
            {
                fehler.Add($"Die Gästezahl muss eine ganze Zahl zwischen 1 und {MaximaleGaestezahl} sein");
            }

            if (AusgewaehlterAnlass == null)
            {
                fehler.Add("Anlass auswählen");
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

        public void TischReservieren()
        {
            // Das Repository wird nur mit fachlich gültigen Werten aufgerufen.
            var validierungsfehler = ErmittleValidierungsfehler();
            if (validierungsfehler.Count > 0)
            {
                ZeigeValidierungsfehler(validierungsfehler);
                return;
            }

            if (!int.TryParse(AnzahlGaeste, out var anzahlGaeste)
                || !TimeOnly.TryParse(Reservierungszeit, out var reservierungszeit))
            {
                ZeigeValidierungsfehler(new[] { "Bitte überprüfen Sie Gästezahl und Reservierungszeit." });
                return;
            }

            var anlass = AusgewaehlterAnlass!;
            var reservierung = new RestaurantReservation
            {
                // Eine Tischreservierung ist nicht zwingend an eine Zimmerreservierung gekoppelt.
                ReservationId = null,
                GuestName = KundenName.Trim(),
                ReservationDate = Reservierungsdatum!.Value,
                ReservationTime = reservierungszeit.ToTimeSpan(),
                GuestCount = anzahlGaeste,
                Occasion = anlass.Bezeichnung,
                // Optionale leere Eingaben werden später als SQL-NULL statt als leerer Text gespeichert.
                SpecialRequests = string.IsNullOrWhiteSpace(Sonderwuensche)
                    ? null
                    : Sonderwuensche.Trim(),
                Status = "Angefragt"
            };

            var personenbezeichnung = anzahlGaeste == 1 ? "Person" : "Personen";
            try
            {
                // Für diesen einzelnen INSERT ist keine mehrstufige Transaktion erforderlich.
                _restaurantReservationRepository.Add(reservierung);
                MessageBox.Show(
                    $"Ihre Tischreservierung für {anzahlGaeste} {personenbezeichnung} am {Reservierungsdatum:dd.MM.yyyy} um {Reservierungszeit} Uhr wurde erfolgreich erfasst.\n"
                    + $"Anlass: {anlass.Bezeichnung}",
                    "Tischreservierung erfasst",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Die Tischreservierung konnte nicht in der Datenbank gespeichert werden. Bitte versuchen Sie es erneut.\n\n{ex.Message}",
                    "Datenbankfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
