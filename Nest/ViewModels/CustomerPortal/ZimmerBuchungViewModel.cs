using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nest.ViewModels.CustomerPortal
{
    // Dieses ViewModel koordiniert Zimmerauswahl, Gastdaten, Validierung und Buchungsablauf.
    public class ZimmerBuchungViewModel : PropertyChangedBase
    {
        // Die Repositories trennen Datenbankzugriffe von der Oberflächen- und Fachlogik.
        private readonly RoomRepository _roomRepo;
        private readonly GastRepository _guestRepo;
        private readonly ReservationRepository _reservationRepo;

        // Die Zuordnung hält Bilddateinamen an einer Stelle und vermeidet Pfade in der View.
        private static readonly IReadOnlyDictionary<string, string> ZimmerbildDateien =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Standard"] = "standard.jpg",
                ["Deluxe"] = "deluxe.jpg",
                ["Suite"] = "suite.jpg",
                ["Executive"] = "executive.jpg"
            };

        private string _vorname = string.Empty;
        private string _nachname = string.Empty;
        private string _nationalitaet = string.Empty;
        private string _email = string.Empty;
        private string _telefon = string.Empty;
        private string _erwachsene = "2";
        private string _kinder = "0";
        private string _sonderwuensche = string.Empty;
        private string _gesamtbetragText = "Gesamtpreis: -- €";
        private string _verfuegbarkeitsHinweis = string.Empty;
        private ObservableCollection<Room> _verfuegbareZimmer = new();
        private Room? _ausgewaehltesZimmer;
        private ImageSource? _zimmerBildQuelle;
        private DateTime? _anreise;
        private DateTime? _abreise;

        public ZimmerBuchungViewModel(
            RoomRepository roomRepo,
            GastRepository guestRepo,
            ReservationRepository resRepo)
        {
            // Konstruktorübergabe macht die benötigten Abhängigkeiten klar erkennbar.
            _roomRepo = roomRepo;
            _guestRepo = guestRepo;
            _reservationRepo = resRepo;

            // Gültige Startwerte ermöglichen sofort eine erste Verfügbarkeitsabfrage.
            _anreise = DateTime.Today.AddDays(1);
            _abreise = DateTime.Today.AddDays(3);
            AktualisiereVerfuegbareZimmer();
        }

        public string Vorname
        {
            get => _vorname;
            set
            {
                _vorname = value;
                NotifyOfPropertyChange(() => Vorname);
            }
        }

        public string Nachname
        {
            get => _nachname;
            set
            {
                _nachname = value;
                NotifyOfPropertyChange(() => Nachname);
            }
        }

        public string Nationalitaet
        {
            get => _nationalitaet;
            set
            {
                _nationalitaet = value;
                NotifyOfPropertyChange(() => Nationalitaet);
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                NotifyOfPropertyChange(() => Email);
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

        public ObservableCollection<Room> VerfuegbareZimmer
        {
            get => _verfuegbareZimmer;
            private set
            {
                _verfuegbareZimmer = value;
                // ObservableCollection meldet Listenänderungen; diese Meldung ersetzt zusätzlich die ganze Liste.
                NotifyOfPropertyChange(() => VerfuegbareZimmer);
            }
        }

        public Room? AusgewaehltesZimmer
        {
            get => _ausgewaehltesZimmer;
            set
            {
                _ausgewaehltesZimmer = value;
                // Von der Auswahl abhängige Anzeige- und Preiswerte müssen gemeinsam aktualisiert werden.
                _zimmerBildQuelle = LadeZimmerBild(value?.Type);
                NotifyOfPropertyChange(() => AusgewaehltesZimmer);
                NotifyOfPropertyChange(() => MaxPersonenInfo);
                NotifyOfPropertyChange(() => ZimmerBildQuelle);
                NotifyOfPropertyChange(() => IstZimmerBildVerfuegbar);
                NotifyOfPropertyChange(() => ZimmerVorschauTitel);
                BerechneGesamtbetrag();
            }
        }

        public DateTime? Anreise
        {
            get => _anreise;
            set
            {
                // DateTime? erlaubt ein leeres DatePicker-Feld; Date entfernt einen unbeabsichtigten Zeitanteil.
                var normalisiertesDatum = value?.Date;
                if (_anreise == normalisiertesDatum)
                {
                    return;
                }

                _anreise = normalisiertesDatum;
                NotifyOfPropertyChange(() => Anreise);
                NotifyOfPropertyChange(() => FruehestesAbreisedatum);
                // Eine Datumsänderung beeinflusst sowohl Verfügbarkeit als auch Gesamtpreis.
                AktualisiereVerfuegbareZimmer();
                BerechneGesamtbetrag();
            }
        }

        public DateTime? Abreise
        {
            get => _abreise;
            set
            {
                var normalisiertesDatum = value?.Date;
                if (_abreise == normalisiertesDatum)
                {
                    return;
                }

                _abreise = normalisiertesDatum;
                NotifyOfPropertyChange(() => Abreise);
                AktualisiereVerfuegbareZimmer();
                BerechneGesamtbetrag();
            }
        }

        public DateTime FruehestesAnreisedatum => DateTime.Today;

        public DateTime FruehestesAbreisedatum => Anreise?.Date.AddDays(1) ?? DateTime.Today.AddDays(1);

        public string Erwachsene
        {
            get => _erwachsene;
            set
            {
                _erwachsene = value;
                NotifyOfPropertyChange(() => Erwachsene);
            }
        }

        public string Kinder
        {
            get => _kinder;
            set
            {
                _kinder = value;
                NotifyOfPropertyChange(() => Kinder);
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

        public string GesamtbetragText
        {
            get => _gesamtbetragText;
            private set
            {
                _gesamtbetragText = value;
                NotifyOfPropertyChange(() => GesamtbetragText);
            }
        }

        public string VerfuegbarkeitsHinweis
        {
            get => _verfuegbarkeitsHinweis;
            private set
            {
                _verfuegbarkeitsHinweis = value;
                NotifyOfPropertyChange(() => VerfuegbarkeitsHinweis);
            }
        }

        public string MaxPersonenInfo
        {
            get
            {
                return AusgewaehltesZimmer == null
                    ? "Bitte wählen Sie ein Zimmer."
                    : $"Maximale Personenzahl: {AusgewaehltesZimmer.Capacity}";
            }
        }

        public ImageSource? ZimmerBildQuelle => _zimmerBildQuelle;

        public bool IstZimmerBildVerfuegbar => ZimmerBildQuelle != null;

        public string ZimmerVorschauTitel => string.IsNullOrWhiteSpace(AusgewaehltesZimmer?.Type)
            ? "Keine Zimmerkategorie ausgewählt"
            : $"Zimmerkategorie: {AusgewaehltesZimmer.Type}";

        private static ImageSource? LadeZimmerBild(string? zimmerTyp)
        {
            // Ein fehlendes Bild darf die eigentliche Zimmerbuchung nicht blockieren.
            if (string.IsNullOrWhiteSpace(zimmerTyp)
                || !ZimmerbildDateien.TryGetValue(zimmerTyp.Trim(), out var dateiname))
            {
                return null;
            }

            var bildPfad = Path.Combine(AppContext.BaseDirectory, "Assets", "Rooms", dateiname);
            if (!File.Exists(bildPfad))
            {
                return null;
            }

            try
            {
                var bild = new BitmapImage();
                bild.BeginInit();
                bild.CacheOption = BitmapCacheOption.OnLoad;
                bild.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bild.UriSource = new Uri(bildPfad, UriKind.Absolute);
                bild.EndInit();
                bild.Freeze();
                return bild;
            }
            catch
            {
                // Bildfehler werden bewusst abgefangen, damit die fachliche Funktion weiter nutzbar bleibt.
                return null;
            }
        }

        private void AktualisiereVerfuegbareZimmer()
        {
            // Ungültige Zeiträume werden vor einem unnötigen Datenbankaufruf abgefangen.
            var zeitraumHinweis = ErmittleZeitraumHinweis();
            if (!string.IsNullOrEmpty(zeitraumHinweis))
            {
                VerfuegbareZimmer = new ObservableCollection<Room>();
                AusgewaehltesZimmer = null;
                VerfuegbarkeitsHinweis = zeitraumHinweis;
                return;
            }

            try
            {
                var bisherAusgewaehlteZimmerId = AusgewaehltesZimmer?.Id;
                // Die Datenbank filtert Zimmer, deren bestehende Buchungen den Zeitraum überschneiden.
                var verfuegbareZimmer = _roomRepo.GetAvailableRooms(Anreise!.Value, Abreise!.Value);
                VerfuegbareZimmer = new ObservableCollection<Room>(verfuegbareZimmer);
                AusgewaehltesZimmer = bisherAusgewaehlteZimmerId.HasValue
                    ? VerfuegbareZimmer.FirstOrDefault(zimmer => zimmer.Id == bisherAusgewaehlteZimmerId.Value)
                    : null;
                VerfuegbarkeitsHinweis = VerfuegbareZimmer.Count == 0
                    ? "Für den ausgewählten Zeitraum sind keine Zimmer verfügbar."
                    : string.Empty;
            }
            catch (Exception ex)
            {
                VerfuegbareZimmer = new ObservableCollection<Room>();
                AusgewaehltesZimmer = null;
                VerfuegbarkeitsHinweis = "Die Zimmerverfügbarkeit konnte nicht geladen werden.";
                MessageBox.Show(
                    $"Die verfügbaren Zimmer konnten nicht geladen werden.\n{ex.Message}",
                    "Datenbankfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string ErmittleZeitraumHinweis()
        {
            if (Anreise == null || Abreise == null)
            {
                return "Bitte wählen Sie Anreise und Abreise aus.";
            }

            if (Anreise.Value.Date < DateTime.Today)
            {
                return "Das Anreisedatum darf nicht in der Vergangenheit liegen.";
            }

            if (Abreise.Value.Date <= Anreise.Value.Date)
            {
                return "Das Abreisedatum muss nach dem Anreisedatum liegen.";
            }

            return string.Empty;
        }

        private void BerechneGesamtbetrag()
        {
            // Der Preis wird nur aus einem vollständigen und gültigen Zeitraum berechnet.
            if (AusgewaehltesZimmer == null || Anreise == null || Abreise == null)
            {
                GesamtbetragText = "Gesamtpreis: -- €";
                return;
            }

            var tage = (Abreise.Value - Anreise.Value).Days;
            if (tage <= 0)
            {
                GesamtbetragText = "Bitte wählen Sie einen gültigen Buchungszeitraum.";
                return;
            }

            var zeiteinheit = tage == 1 ? "Nacht" : "Nächte";
            // Geldbeträge sind decimal, um binäre Rundungsfehler von double zu vermeiden.
            GesamtbetragText = $"Gesamtpreis für {tage} {zeiteinheit}: {tage * AusgewaehltesZimmer.PricePerNight:F2} €";
        }

        public void Increase()
        {
            if (int.TryParse(Erwachsene, out var aktuelleAnzahl))
            {
                var maximaleAnzahl = AusgewaehltesZimmer?.Capacity ?? 10;
                if (aktuelleAnzahl + 1 <= maximaleAnzahl)
                {
                    Erwachsene = (aktuelleAnzahl + 1).ToString();
                }
            }
        }

        public void Decrease()
        {
            if (int.TryParse(Erwachsene, out var aktuelleAnzahl) && aktuelleAnzahl > 1)
            {
                Erwachsene = (aktuelleAnzahl - 1).ToString();
            }
        }

        public void KinderIncrease()
        {
            // Erwachsene und Kinder dürfen gemeinsam die Zimmerkapazität nicht überschreiten.
            if (!int.TryParse(Kinder, out var aktuelleKinderzahl)
                || !int.TryParse(Erwachsene, out var erwachsene))
            {
                return;
            }

            var maximaleAnzahl = AusgewaehltesZimmer?.Capacity ?? 10;
            var freiePlaetze = maximaleAnzahl - erwachsene;
            if (aktuelleKinderzahl + 1 <= freiePlaetze)
            {
                Kinder = (aktuelleKinderzahl + 1).ToString();
            }
        }

        public void KinderDecrease()
        {
            if (int.TryParse(Kinder, out var aktuelleKinderzahl) && aktuelleKinderzahl > 0)
            {
                Kinder = (aktuelleKinderzahl - 1).ToString();
            }
        }

        private List<string> ErmittleZimmerbuchungsfehler()
        {
            // TryParse prüft Texteingaben sicher; alle Fehler werden für eine gemeinsame Meldung gesammelt.
            var fehler = new List<string>();

            if (string.IsNullOrWhiteSpace(Vorname))
            {
                fehler.Add("Vorname eingeben");
            }

            if (string.IsNullOrWhiteSpace(Nachname))
            {
                fehler.Add("Nachname eingeben");
            }

            if (AusgewaehltesZimmer == null)
            {
                fehler.Add("Zimmer auswählen");
            }

            if (Anreise == null)
            {
                fehler.Add("Gültiges Anreisedatum auswählen");
            }
            else if (Anreise.Value.Date < DateTime.Today)
            {
                fehler.Add("Das Anreisedatum darf nicht in der Vergangenheit liegen.");
            }

            if (Abreise == null)
            {
                fehler.Add("Gültiges Abreisedatum auswählen");
            }
            else if (Anreise != null && Abreise.Value.Date <= Anreise.Value.Date)
            {
                fehler.Add("Das Abreisedatum muss nach dem Anreisedatum liegen.");
            }

            var erwachseneGueltig = int.TryParse(Erwachsene, out var erwachsene) && erwachsene > 0;
            if (!erwachseneGueltig)
            {
                fehler.Add("Gültige Anzahl der Erwachsenen eingeben");
            }

            var kinderGueltig = int.TryParse(Kinder, out var kinder) && kinder >= 0;
            if (!kinderGueltig)
            {
                fehler.Add("Gültige Anzahl der Kinder eingeben");
            }

            if (AusgewaehltesZimmer != null
                && erwachseneGueltig
                && kinderGueltig
                && erwachsene + kinder > AusgewaehltesZimmer.Capacity)
            {
                fehler.Add("Die Zimmerkapazität darf nicht überschritten werden");
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

        public void ZimmerBuchen()
        {
            // Ohne erfolgreiche Validierung wird weder ein Model erzeugt noch die Datenbank verändert.
            var validierungsfehler = ErmittleZimmerbuchungsfehler();
            if (validierungsfehler.Count > 0)
            {
                ZeigeValidierungsfehler(validierungsfehler);
                return;
            }

            // Nach der Prüfung sind diese nullable Werte gesetzt; ! unterdrückt nur die Compilerwarnung.
            var zimmer = AusgewaehltesZimmer!;
            var anreise = Anreise!.Value;
            var abreise = Abreise!.Value;
            _ = int.TryParse(Erwachsene, out var erwachsene);
            _ = int.TryParse(Kinder, out var kinder);

            try
            {
                // Die erste Prüfung gibt dem Benutzer schnell Rückmeldung.
                // Der verbindliche Schutz erfolgt später nochmals innerhalb einer Transaktion.
                if (!_reservationRepo.IsRoomAvailable(zimmer.Id, anreise, abreise))
                {
                    BehandleNichtMehrVerfuegbaresZimmer();
                    return;
                }

                // Bestehende Gäste werden wiederverwendet, um unnötige Dubletten zu vermeiden.
                var gast = _guestRepo.GetByEmail(Email) ?? _guestRepo.GetByPhone(Telefon);
                if (gast == null)
                {
                    gast = new Guest
                    {
                        FirstName = Vorname,
                        LastName = Nachname,
                        Email = Email,
                        Phone = Telefon,
                        Nationality = Nationalitaet,
                        DateAdded = DateTime.Now
                    };
                    _guestRepo.Add(gast);
                }

                var naechte = (abreise - anreise).Days;
                var gesamtbetrag = naechte * zimmer.PricePerNight;
                var reservierung = new Reservation
                {
                    GuestId = gast.Id,
                    RoomId = zimmer.Id,
                    CheckInDate = anreise,
                    CheckOutDate = abreise,
                    Nights = naechte,
                    Adults = erwachsene,
                    Children = kinder,
                    TotalAmount = gesamtbetrag,
                    AmountPaid = 0,
                    PaymentMethod = "Bar",
                    Status = "Reserviert",
                    // Ein kurzer Teil einer GUID erzeugt einen gut nutzbaren, praktisch eindeutigen Buchungscode.
                    ReservationCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    DateCreated = DateTime.Now,
                    SpecialRequests = Sonderwuensche,
                    ReceptionistId = null
                };

                // Diese Methode prüft unter einer Sperre erneut und verhindert parallele Doppelbuchungen.
                if (!_reservationRepo.TryAddIfRoomAvailable(reservierung))
                {
                    BehandleNichtMehrVerfuegbaresZimmer();
                    return;
                }

                MessageBox.Show(
                    $"Die Zimmerbuchung wurde erfolgreich gespeichert.\nReservierungscode: {reservierung.ReservationCode}",
                    "Buchung erfolgreich",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Nach Erfolg werden sensible und alte Eingaben für die nächste Buchung zurückgesetzt.
                Vorname = string.Empty;
                Nachname = string.Empty;
                Nationalitaet = string.Empty;
                Email = string.Empty;
                Telefon = string.Empty;
                Sonderwuensche = string.Empty;
                Erwachsene = "2";
                Kinder = "0";
                AktualisiereVerfuegbareZimmer();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Die Zimmerbuchung konnte nicht gespeichert werden.\n{ex.Message}",
                    "Buchung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BehandleNichtMehrVerfuegbaresZimmer()
        {
            MessageBox.Show(
                "Dieses Zimmer ist im ausgewählten Zeitraum bereits reserviert. Bitte wählen Sie ein anderes Zimmer.",
                "Zimmer nicht verfügbar",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            AktualisiereVerfuegbareZimmer();
        }
    }
}
