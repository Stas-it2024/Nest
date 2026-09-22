using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using Nest.Services;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace Nest.ViewModels.CustomerPortal
{
    // Dieses ViewModel bildet eine lokale Demo-Zahlung ab; es besteht keine Verbindung zu einer echten Bank.
    public class OnlineZahlungViewModel : PropertyChangedBase
    {
        private static readonly CultureInfo DeutscheKultur = CultureInfo.GetCultureInfo("de-DE");

        private readonly ReservationRepository _reservationRepository;
        private readonly PdfTicketService _pdfTicketService;
        private string _reservierungscode = string.Empty;
        private string _verwendungszweck = string.Empty;
        private string _kontoinhaber = string.Empty;
        private string _bankname = string.Empty;
        private string _iban = string.Empty;
        private string _zahlungsbetrag = string.Empty;
        private string _statusmeldung = "Bitte laden Sie zuerst Ihre Reservierung.";
        private string _letzterPdfPfad = string.Empty;
        private string _letzteBelegnummer = string.Empty;
        private ReservationOverview? _geladeneReservierung;

        public OnlineZahlungViewModel(ReservationRepository reservationRepository)
            : this(reservationRepository, new PdfTicketService())
        {
        }

        public OnlineZahlungViewModel(
            ReservationRepository reservationRepository,
            PdfTicketService pdfTicketService)
        {
            // Repository und PDF-Service bleiben getrennt, damit Datenhaltung und Dokumenterstellung
            // unabhängig voneinander entwickelt und getestet werden können.
            _reservationRepository = reservationRepository;
            _pdfTicketService = pdfTicketService;
        }

        public string Reservierungscode
        {
            get => _reservierungscode;
            set
            {
                // Trim entfernt äußere Leerzeichen; Großbuchstaben sorgen für eine einheitliche Suche.
                var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
                if (_reservierungscode == normalized)
                {
                    return;
                }

                _reservierungscode = normalized;
                // Nach einer Codeänderung dürfen keine Daten der vorherigen Reservierung angezeigt werden.
                SetGeladeneReservierung(null);
                Statusmeldung = "Bitte laden Sie zuerst Ihre Reservierung.";
                NotifyOfPropertyChange(() => Reservierungscode);
            }
        }

        public string Verwendungszweck
        {
            get => _verwendungszweck;
            set
            {
                _verwendungszweck = value ?? string.Empty;
                NotifyOfPropertyChange(() => Verwendungszweck);
            }
        }

        public string Kontoinhaber
        {
            get => _kontoinhaber;
            set
            {
                _kontoinhaber = value ?? string.Empty;
                NotifyOfPropertyChange(() => Kontoinhaber);
            }
        }

        public string Bankname
        {
            get => _bankname;
            set
            {
                _bankname = value ?? string.Empty;
                NotifyOfPropertyChange(() => Bankname);
            }
        }

        public string Iban
        {
            get => _iban;
            set
            {
                // Intern wird normalisiert, in der View aber lesbar in Vierergruppen dargestellt.
                _iban = FormatiereIban(value);
                NotifyOfPropertyChange(() => Iban);
            }
        }

        public string Zahlungsbetrag
        {
            get => _zahlungsbetrag;
            set
            {
                _zahlungsbetrag = value ?? string.Empty;
                NotifyOfPropertyChange(() => Zahlungsbetrag);
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

        public string GeladenerReservierungscode => _geladeneReservierung?.ReservationCode ?? "–";
        public string GastInfo => _geladeneReservierung?.Guest ?? "–";
        public string ZimmerInfo => _geladeneReservierung == null
            ? "–"
            : $"{_geladeneReservierung.Room} ({_geladeneReservierung.Type})";
        public string GesamtbetragText => FormatiereGeld(_geladeneReservierung?.TotalAmount);
        public string BereitsBezahltText => FormatiereGeld(_geladeneReservierung?.AmountPaid);
        // Abgeleitete Properties liefern direkt formatierte Werte für die gebundene View.
        public string OffenerBetragText => FormatiereGeld(_geladeneReservierung?.OutstandingAmount);
        public string ReservierungsstatusText => _geladeneReservierung?.Status ?? "–";
        public string LetzterPdfPfad => _letzterPdfPfad;
        public string LetzterPdfPfadText => string.IsNullOrWhiteSpace(LetzterPdfPfad)
            ? "Noch kein PDF-Zahlungsbeleg erstellt."
            : $"Letzter PDF-Zahlungsbeleg: {LetzterPdfPfad}";
        public string LetzteBelegnummer => _letzteBelegnummer;
        public bool IstPdfVerfuegbar => !string.IsNullOrWhiteSpace(LetzterPdfPfad)
            && File.Exists(LetzterPdfPfad);
        public bool CanPdfOeffnen => IstPdfVerfuegbar;

        public void ReservierungLaden()
        {
            // Die Daten werden bewusst erst per Button geladen und nicht bei jedem Tastendruck.
            if (string.IsNullOrWhiteSpace(Reservierungscode))
            {
                ZeigeFehler("Bitte geben Sie einen Reservierungscode ein.");
                return;
            }

            try
            {
                var reservation = _reservationRepository.GetPaymentDetailsByCode(Reservierungscode);
                if (reservation == null)
                {
                    SetGeladeneReservierung(null);
                    ZeigeFehler("Die Reservierung wurde nicht gefunden.");
                    return;
                }

                // SetGeladeneReservierung meldet alle abhängigen Anzeigefelder an die View.
                SetGeladeneReservierung(reservation);
                Zahlungsbetrag = reservation.OutstandingAmount > 0m
                    ? reservation.OutstandingAmount.ToString("N2", DeutscheKultur)
                    : string.Empty;
                Statusmeldung = reservation.OutstandingAmount <= 0m
                    ? "Diese Reservierung ist bereits vollständig bezahlt."
                    : "Die Reservierung wurde geladen.";
            }
            catch
            {
                ZeigeFehler("Die Reservierung konnte nicht geladen werden. Bitte versuchen Sie es erneut.");
            }
        }

        public void OnlineBezahlen()
        {
            // Lokale Validierung verhindert unnötige Datenbankzugriffe mit unbrauchbaren Eingaben.
            var validationMessage = ErmittleValidierungsfehler(out var paymentAmount);
            if (validationMessage != null)
            {
                ZeigeFehler(validationMessage);
                return;
            }

            try
            {
                var currentReservation = _reservationRepository.GetPaymentDetailsByCode(Reservierungscode);
                if (currentReservation == null)
                {
                    SetGeladeneReservierung(null);
                    ZeigeFehler("Die Reservierung wurde nicht gefunden.");
                    return;
                }

                SetGeladeneReservierung(currentReservation);
                if (currentReservation.OutstandingAmount <= 0m)
                {
                    ZeigeFehler("Diese Reservierung ist bereits vollständig bezahlt.");
                    return;
                }

                if (paymentAmount > currentReservation.OutstandingAmount)
                {
                    ZeigeFehler("Der Zahlungsbetrag darf den offenen Betrag nicht überschreiten.");
                    return;
                }

                Statusmeldung = "Zahlung wird verarbeitet...";
                // Das Repository wiederholt die betragskritischen Prüfungen unter einer Datenbanksperre.
                var result = _reservationRepository.TryApplyOnlinePayment(
                    Reservierungscode,
                    paymentAmount,
                    out var updatedReservation);

                if (updatedReservation != null)
                {
                    SetGeladeneReservierung(updatedReservation);
                }

                switch (result)
                {
                    case PaymentUpdateResult.Success:
                        BehandleErfolgreicheZahlungNachDatenbank(paymentAmount, updatedReservation!);
                        return;
                    case PaymentUpdateResult.NotFound:
                        SetGeladeneReservierung(null);
                        ZeigeFehler("Die Reservierung wurde nicht gefunden.");
                        return;
                    case PaymentUpdateResult.FullyPaid:
                        ZeigeFehler("Diese Reservierung ist bereits vollständig bezahlt.");
                        return;
                    case PaymentUpdateResult.AmountExceedsOutstanding:
                        ZeigeFehler("Der Zahlungsbetrag ist höher als der offene Betrag.");
                        return;
                    default:
                        ZeigeFehler("Bitte geben Sie einen gültigen Zahlungsbetrag ein.");
                        return;
                }
            }
            catch
            {
                ZeigeFehler("Die Zahlung konnte nicht verarbeitet werden. Bitte versuchen Sie es erneut.");
            }
        }

        internal string? ErmittleValidierungsfehler(out decimal paymentAmount)
        {
            // Die nullable Rückgabe ist null bei Erfolg oder enthält genau eine verständliche Fehlermeldung.
            paymentAmount = 0m;

            if (string.IsNullOrWhiteSpace(Kontoinhaber))
            {
                return "Bitte geben Sie den Namen des Kontoinhabers ein.";
            }

            if (string.IsNullOrWhiteSpace(Bankname))
            {
                return "Bitte geben Sie den Namen der Bank ein.";
            }

            if (string.IsNullOrWhiteSpace(Iban))
            {
                return "Bitte geben Sie eine IBAN ein.";
            }

            if (!IstGueltigeIban(Iban))
            {
                return "Die eingegebene IBAN ist ungültig.";
            }

            // decimal und deutsche Kultur erlauben Geldbeträge wie 123,45 ohne double-Rundungsfehler.
            if (!decimal.TryParse(
                Zahlungsbetrag,
                NumberStyles.Number,
                DeutscheKultur,
                out paymentAmount)
                || paymentAmount <= 0m
                || decimal.Round(paymentAmount, 2) != paymentAmount)
            {
                return "Bitte geben Sie einen gültigen Zahlungsbetrag ein.";
            }

            if (string.IsNullOrWhiteSpace(Reservierungscode))
            {
                return "Bitte geben Sie einen Reservierungscode ein.";
            }

            return null;
        }

        public static bool IstGueltigeIban(string? iban)
        {
            // Die standardisierte Modulo-97-Prüfung erkennt formal ungültige IBANs ohne Bankzugriff.
            var normalized = NormalisiereIban(iban);
            if (normalized.Length < 15 || normalized.Length > 34
                || !IstAsciiBuchstabe(normalized[0])
                || !IstAsciiBuchstabe(normalized[1])
                || !IstAsciiZiffer(normalized[2])
                || !IstAsciiZiffer(normalized[3])
                || normalized.Any(character => !IstAsciiBuchstabe(character) && !IstAsciiZiffer(character)))
            {
                return false;
            }

            // Für die IBAN-Prüfung werden die ersten vier Zeichen normgerecht ans Ende verschoben.
            var rearranged = normalized[4..] + normalized[..4];
            var remainder = 0;
            foreach (var character in rearranged)
            {
                if (char.IsDigit(character))
                {
                    remainder = (remainder * 10 + (character - '0')) % 97;
                    continue;
                }

                var letterValue = character - 'A' + 10;
                remainder = (remainder * 100 + letterValue) % 97;
            }

            return remainder == 1;
        }

        public void PdfOeffnen()
        {
            if (!IstPdfVerfuegbar)
            {
                Statusmeldung = "Der PDF-Zahlungsbeleg wurde nicht gefunden.";
                NotifyPdfStatus();
                return;
            }

            if (_pdfTicketService.VersuchePdfZuOeffnen(LetzterPdfPfad))
            {
                Statusmeldung = "Der PDF-Zahlungsbeleg wurde geöffnet.";
                return;
            }

            Statusmeldung = $"Der PDF-Zahlungsbeleg konnte nicht automatisch geöffnet werden. Gespeichert unter: {LetzterPdfPfad}";
            MessageBox.Show(
                $"Der PDF-Zahlungsbeleg konnte nicht automatisch geöffnet werden.\n\nGespeichert unter:\n{LetzterPdfPfad}",
                "PDF-Zahlungsbeleg",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BehandleErfolgreicheZahlungNachDatenbank(
            decimal paymentAmount,
            ReservationOverview transactionResult)
        {
            // Erst die erfolgreiche Datenbankänderung wird übernommen; danach werden sensible Felder geleert.
            SetGeladeneReservierung(transactionResult);
            LeereSensibleZahlungsdaten();

            ReservationOverview? aktuelleReservierung;
            try
            {
                aktuelleReservierung = _reservationRepository.GetPaymentDetailsByCode(
                    transactionResult.ReservationCode);
            }
            catch
            {
                aktuelleReservierung = null;
            }

            if (aktuelleReservierung == null)
            {
                BehandlePdfFehlerNachErfolgreicherZahlung();
                return;
            }

            SetGeladeneReservierung(aktuelleReservierung);
            var isFullyPaid = aktuelleReservierung.OutstandingAmount <= 0m;
            Zahlungsbetrag = isFullyPaid
                ? string.Empty
                : aktuelleReservierung.OutstandingAmount.ToString("N2", DeutscheKultur);

            // Der Beleg wird absichtlich erst nach dem Datenbank-Commit erstellt.
            // Ein PDF-Fehler darf eine bereits erfolgreiche Zahlung nicht rückgängig machen.
            PdfTicketResult pdfResult;
            try
            {
                pdfResult = _pdfTicketService.ErstelleZahlungsbeleg(
                    aktuelleReservierung,
                    paymentAmount);
            }
            catch
            {
                BehandlePdfFehlerNachErfolgreicherZahlung();
                return;
            }

            SetPdfResult(pdfResult);
            // Windows öffnet den Beleg anschließend mit dem standardmäßig verknüpften PDF-Programm.
            var wurdeGeoeffnet = _pdfTicketService.VersuchePdfZuOeffnen(pdfResult.Dateipfad);
            Statusmeldung = isFullyPaid
                ? "Die Zahlung wurde erfolgreich verarbeitet. Der PDF-Zahlungsbeleg wurde erstellt. Die Reservierung ist vollständig bezahlt."
                : "Die Zahlung wurde erfolgreich verarbeitet. Der PDF-Zahlungsbeleg wurde erstellt.";

            var message = $"Die Zahlung wurde erfolgreich verarbeitet.\n"
                + $"Reservierung: {aktuelleReservierung.ReservationCode}\n"
                + $"Betrag: {paymentAmount.ToString("N2", DeutscheKultur)} €\n\n"
                + "Der PDF-Zahlungsbeleg wurde erstellt.\n"
                + $"Gespeichert unter:\n{pdfResult.Dateipfad}\n\n"
                + "Demo-Zahlung – keine reale Banktransaktion.";
            if (isFullyPaid)
            {
                message += "\n\nDie Reservierung ist vollständig bezahlt.";
            }

            if (!wurdeGeoeffnet)
            {
                message += "\n\nDer Beleg konnte nicht automatisch geöffnet werden. Sie können ihn über die Schaltfläche im Zahlungsbereich öffnen.";
            }

            MessageBox.Show(
                message,
                "Online-Zahlung erfolgreich",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BehandlePdfFehlerNachErfolgreicherZahlung()
        {
            Statusmeldung = "Die Zahlung wurde erfolgreich verarbeitet, aber der PDF-Beleg konnte nicht erstellt werden.";
            MessageBox.Show(
                "Die Zahlung wurde erfolgreich verarbeitet, aber der PDF-Beleg konnte nicht erstellt werden.\n\n"
                + "Die Zahlung bleibt gespeichert. Bitte wenden Sie sich bei Bedarf an die Rezeption.",
                "Zahlung erfolgreich - PDF-Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void SetPdfResult(PdfTicketResult result)
        {
            _letzterPdfPfad = result.Dateipfad;
            _letzteBelegnummer = result.Belegnummer;
            NotifyPdfStatus();
        }

        private void NotifyPdfStatus()
        {
            NotifyOfPropertyChange(() => LetzterPdfPfad);
            NotifyOfPropertyChange(() => LetzterPdfPfadText);
            NotifyOfPropertyChange(() => LetzteBelegnummer);
            NotifyOfPropertyChange(() => IstPdfVerfuegbar);
            NotifyOfPropertyChange(() => CanPdfOeffnen);
        }

        private void SetGeladeneReservierung(ReservationOverview? reservation)
        {
            _geladeneReservierung = reservation;
            // Da mehrere Textfelder von demselben Objekt abhängen, werden alle Bindings informiert.
            NotifyOfPropertyChange(() => GeladenerReservierungscode);
            NotifyOfPropertyChange(() => GastInfo);
            NotifyOfPropertyChange(() => ZimmerInfo);
            NotifyOfPropertyChange(() => GesamtbetragText);
            NotifyOfPropertyChange(() => BereitsBezahltText);
            NotifyOfPropertyChange(() => OffenerBetragText);
            NotifyOfPropertyChange(() => ReservierungsstatusText);
        }

        private void ZeigeFehler(string message)
        {
            Statusmeldung = message;
            MessageBox.Show(
                message,
                "Online-Zahlung",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        internal void LeereSensibleZahlungsdaten()
        {
            // Kontodaten bleiben nach erfolgreicher Verarbeitung nicht in der Oberfläche stehen.
            Kontoinhaber = string.Empty;
            Bankname = string.Empty;
            Iban = string.Empty;
        }

        private static string FormatiereIban(string? iban)
        {
            var normalized = NormalisiereIban(iban);
            return string.Join(
                " ",
                Enumerable.Range(0, (normalized.Length + 3) / 4)
                    .Select(index => normalized.Substring(
                        index * 4,
                        Math.Min(4, normalized.Length - index * 4))));
        }

        private static string NormalisiereIban(string? iban)
        {
            // Alle Leerzeichen werden entfernt und Buchstaben vereinheitlicht, bevor geprüft wird.
            return new string((iban ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character))
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static bool IstAsciiBuchstabe(char character)
        {
            return character is >= 'A' and <= 'Z';
        }

        private static bool IstAsciiZiffer(char character)
        {
            return character is >= '0' and <= '9';
        }

        private static string FormatiereGeld(decimal? amount)
        {
            return amount.HasValue
                ? $"{amount.Value.ToString("N2", DeutscheKultur)} €"
                : "–";
        }
    }
}
