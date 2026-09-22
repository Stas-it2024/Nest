using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System.Collections.ObjectModel;
using System.Net.Mail;
using System.Windows;

namespace Nest.ViewModels.CustomerPortal
{
    // Dieses ViewModel verbindet die Veranstaltungsliste und Ticketaktionen mit dem EventRepository.
    public class VeranstaltungenViewModel : PropertyChangedBase
    {
        private readonly EventRepository _eventRepository;
        private string _kundenName;
        private string _kundenEmail = string.Empty;
        private string _kundenTelefon = string.Empty;
        private string _anzahlTickets = "1";
        private string _statusmeldung = string.Empty;
        private bool _buchungLaeuft;

        public VeranstaltungenViewModel()
            : this(new EventRepository(), null)
        {
        }

        public VeranstaltungenViewModel(EventRepository eventRepository, string? gastname)
        {
            _eventRepository = eventRepository;
            // Der aus der Kundenseite übergebene Name erspart eine erneute Eingabe.
            _kundenName = gastname?.Trim() ?? string.Empty;
            LadeVeranstaltungen();
        }

        // Die ObservableCollection aktualisiert die gebundene Liste beim Laden automatisch.
        public ObservableCollection<Veranstaltung> Veranstaltungen { get; } = new();

        public string KundenName
        {
            get => _kundenName;
            set
            {
                _kundenName = value ?? string.Empty;
                NotifyOfPropertyChange(() => KundenName);
            }
        }

        public string KundenEmail
        {
            get => _kundenEmail;
            set
            {
                _kundenEmail = value ?? string.Empty;
                NotifyOfPropertyChange(() => KundenEmail);
            }
        }

        public string KundenTelefon
        {
            get => _kundenTelefon;
            set
            {
                _kundenTelefon = value ?? string.Empty;
                NotifyOfPropertyChange(() => KundenTelefon);
            }
        }

        public string AnzahlTickets
        {
            get => _anzahlTickets;
            set
            {
                _anzahlTickets = value ?? string.Empty;
                NotifyOfPropertyChange(() => AnzahlTickets);
            }
        }

        public string Statusmeldung
        {
            get => _statusmeldung;
            private set
            {
                _statusmeldung = value;
                NotifyOfPropertyChange(() => Statusmeldung);
            }
        }

        public void LadeVeranstaltungen()
        {
            // Die View erhält nur kommende, aktuell aus der Datenbank geladene Veranstaltungen.
            try
            {
                var aktuelleVeranstaltungen = _eventRepository.GetUpcomingEvents();
                Veranstaltungen.Clear();
                foreach (var veranstaltung in aktuelleVeranstaltungen)
                {
                    Veranstaltungen.Add(veranstaltung);
                }

                Statusmeldung = Veranstaltungen.Count == 0
                    ? "Derzeit sind keine kommenden Veranstaltungen verfügbar."
                    : $"{Veranstaltungen.Count} kommende Veranstaltungen geladen.";
            }
            catch (Exception ex)
            {
                Veranstaltungen.Clear();
                Statusmeldung = "Die Veranstaltungen konnten nicht aus der Datenbank geladen werden.";
                MessageBox.Show(
                    $"Die Veranstaltungen konnten nicht geladen werden.\n{ex.Message}",
                    "Datenbankfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void TicketKaufen(Veranstaltung? veranstaltung)
        {
            // Die Prüfung im ViewModel liefert schnelle, verständliche Rückmeldungen.
            // Das Repository prüft kritische Werte innerhalb der Transaktion erneut.
            var validierungsfehler = ErmittleValidierungsfehler(veranstaltung, out var anzahl);
            if (validierungsfehler.Count > 0)
            {
                ZeigeValidierungsfehler(validierungsfehler);
                return;
            }

            // Während der Verarbeitung wird der Kauf deaktiviert, um Doppelklicks zu vermeiden.
            _buchungLaeuft = true;
            NotifyOfPropertyChange(nameof(CanTicketKaufen));

            try
            {
                var result = _eventRepository.BookTickets(new TicketBookingRequest
                {
                    EventId = veranstaltung!.Id,
                    GuestName = KundenName,
                    GuestEmail = KundenEmail,
                    GuestPhone = KundenTelefon,
                    Quantity = anzahl,
                    // Die Ticketbestellung ist optional mit einer Zimmerreservierung verknüpft.
                    ReservationId = null
                });

                if (result.Status != TicketBookingStatus.Success)
                {
                    ZeigeRepositoryFehler(result);
                    LadeVeranstaltungen();
                    return;
                }

                LadeVeranstaltungen();
                Statusmeldung =
                    $"Ticketbestellung erfolgreich: {result.TicketCode} – Gesamtpreis {result.TotalPrice:F2} €.";
                MessageBox.Show(
                    $"Ihre Ticketbestellung für „{veranstaltung.Titel}“ wurde gespeichert.\n\n"
                    + $"Ticketcode: {result.TicketCode}\n"
                    + $"Anzahl: {anzahl}\n"
                    + $"Einzelpreis: {result.UnitPrice:F2} €\n"
                    + $"Gesamtpreis: {result.TotalPrice:F2} €\n"
                    + $"Verbleibende Tickets: {result.RemainingTickets}",
                    "Ticketbestellung erfolgreich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Statusmeldung = "Die Ticketbestellung wurde nicht gespeichert.";
                MessageBox.Show(
                    $"Die Ticketbestellung konnte nicht gespeichert werden. Alle Änderungen wurden zurückgesetzt.\n{ex.Message}",
                    "Datenbankfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // finally wird bei Erfolg und Fehler ausgeführt und gibt den Button zuverlässig wieder frei.
                _buchungLaeuft = false;
                NotifyOfPropertyChange(nameof(CanTicketKaufen));
            }
        }

        public bool CanTicketKaufen(Veranstaltung? veranstaltung)
        {
            // Caliburn.Micro nutzt diese Methode, um die zugehörige Aktion nur bei gültigem Zustand zu erlauben.
            return !_buchungLaeuft
                && veranstaltung != null
                && veranstaltung.Startzeit >= DateTime.Now
                && veranstaltung.VerfuegbareTickets > 0;
        }

        private List<string> ErmittleValidierungsfehler(
            Veranstaltung? veranstaltung,
            out int anzahl)
        {
            // Neben Pflichtfeldern werden E-Mail-Format, Ganzzahl und sichtbarer Ticketbestand geprüft.
            var fehler = new List<string>();
            anzahl = 0;

            if (veranstaltung == null)
            {
                fehler.Add("Bitte wählen Sie eine Veranstaltung aus.");
            }
            else
            {
                if (veranstaltung.Startzeit < DateTime.Now)
                {
                    fehler.Add("Die ausgewählte Veranstaltung liegt bereits in der Vergangenheit.");
                }

                if (veranstaltung.Id <= 0)
                {
                    fehler.Add("Die ausgewählte Veranstaltung ist ungültig.");
                }
            }

            if (string.IsNullOrWhiteSpace(KundenName))
            {
                fehler.Add("Bitte geben Sie Ihren Namen ein.");
            }

            if (string.IsNullOrWhiteSpace(KundenEmail))
            {
                fehler.Add("Bitte geben Sie Ihre E-Mail-Adresse ein.");
            }
            else if (!MailAddress.TryCreate(KundenEmail.Trim(), out _))
            {
                fehler.Add("Bitte geben Sie eine gültige E-Mail-Adresse ein.");
            }

            if (string.IsNullOrWhiteSpace(KundenTelefon))
            {
                fehler.Add("Bitte geben Sie Ihre Telefonnummer ein.");
            }

            if (!int.TryParse(AnzahlTickets, out anzahl) || anzahl <= 0)
            {
                fehler.Add("Die Ticketanzahl muss eine ganze Zahl größer als 0 sein.");
            }
            else if (veranstaltung != null && anzahl > veranstaltung.VerfuegbareTickets)
            {
                fehler.Add(
                    $"Es sind nur noch {veranstaltung.VerfuegbareTickets} Tickets verfügbar.");
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

        private static void ZeigeRepositoryFehler(TicketBookingResult result)
        {
            var meldung = result.Status switch
            {
                TicketBookingStatus.EventNotFound =>
                    "Die ausgewählte Veranstaltung existiert nicht mehr.",
                TicketBookingStatus.EventInPast =>
                    "Die ausgewählte Veranstaltung liegt bereits in der Vergangenheit.",
                TicketBookingStatus.InvalidQuantity =>
                    "Die Ticketanzahl muss größer als 0 sein.",
                TicketBookingStatus.InsufficientTickets =>
                    $"Die gewünschte Anzahl ist nicht verfügbar. Verbleibend: {result.RemainingTickets}.",
                TicketBookingStatus.MissingGuestName =>
                    "Bitte geben Sie Ihren Namen ein.",
                TicketBookingStatus.MissingGuestEmail =>
                    "Bitte geben Sie Ihre E-Mail-Adresse ein.",
                TicketBookingStatus.MissingGuestPhone =>
                    "Bitte geben Sie Ihre Telefonnummer ein.",
                _ => "Die Ticketbestellung konnte nicht durchgeführt werden."
            };

            MessageBox.Show(
                meldung,
                "Ticketbestellung nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
