using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Nest.Models;
using PdfSharp.Pdf;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Nest.Services
{
    // Das Ergebnisobjekt gibt dem ViewModel den erzeugten Pfad und die Beleginformationen zurück.
    public sealed class PdfTicketResult
    {
        public required string Dateipfad { get; init; }
        public required string Belegnummer { get; init; }
        public DateTime ErstelltAm { get; init; }
    }

    public class PdfTicketService
    {
        // Farben und Kultur stehen zentral, damit alle Belege einheitlich formatiert werden.
        private static readonly CultureInfo DeutscheKultur = CultureInfo.GetCultureInfo("de-DE");
        private static readonly Color HotelBlau = Color.FromRgb(23, 58, 103);
        private static readonly Color HellesBlau = Color.FromRgb(238, 245, 252);
        private static readonly Color RahmenBlau = Color.FromRgb(200, 217, 235);
        private static readonly Color Dunkelgrau = Color.FromRgb(31, 41, 55);
        private static readonly Color Sekundaergrau = Color.FromRgb(91, 100, 114);
        private static readonly Color Erfolgsgruen = Color.FromRgb(46, 125, 50);
        private static readonly Color Teilzahlungsorange = Color.FromRgb(178, 106, 0);

        public PdfTicketResult ErstelleZahlungsbeleg(
            ReservationOverview reservation,
            decimal paymentAmount)
        {
            // Ohne gültige Reservierungs- und Zahlungsdaten darf kein irreführender Beleg entstehen.
            ArgumentNullException.ThrowIfNull(reservation);
            if (string.IsNullOrWhiteSpace(reservation.ReservationCode)
                || paymentAmount <= 0m)
            {
                throw new ArgumentException("Für den Zahlungsbeleg fehlen gültige Zahlungsdaten.");
            }

            var erstelltAm = DateTime.Now;
            var belegnummer = ErzeugeBelegnummer(erstelltAm);
            // Der persönliche Dokumente-Ordner ist für den Benutzer leicht auffindbar und nicht installationsabhängig.
            var dokumenteOrdner = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(dokumenteOrdner))
            {
                throw new IOException("Der Dokumente-Ordner konnte nicht ermittelt werden.");
            }

            var ticketOrdner = Path.Combine(dokumenteOrdner, "GrandHorizonHotel", "Tickets");
            Directory.CreateDirectory(ticketOrdner);

            // Ungültige Dateinamenzeichen werden entfernt, damit Save auf Windows zuverlässig funktioniert.
            var sichererReservierungscode = BereinigeDateinamenTeil(reservation.ReservationCode);
            var dateiname = $"GrandHorizon_Ticket_{sichererReservierungscode}_{erstelltAm:yyyyMMdd_HHmmss}_{belegnummer[^8..]}.pdf";
            var dateipfad = Path.Combine(ticketOrdner, dateiname);

            // MigraDoc beschreibt Inhalt und Layout; PDFsharp rendert daraus die tatsächliche PDF-Datei.
            var document = ErzeugeDokument(reservation, paymentAmount, belegnummer, erstelltAm);
            var renderer = new PdfDocumentRenderer
            {
                Document = document
            };
            renderer.PdfDocument.PageLayout = PdfPageLayout.SinglePage;
            renderer.PdfDocument.ViewerPreferences.FitWindow = true;
            // RenderDocument erzeugt das PDF im Speicher, Save schreibt es anschließend an den ermittelten Pfad.
            renderer.RenderDocument();
            renderer.Save(dateipfad);

            if (!File.Exists(dateipfad))
            {
                throw new IOException("Der Zahlungsbeleg konnte nicht gespeichert werden.");
            }

            return new PdfTicketResult
            {
                Dateipfad = dateipfad,
                Belegnummer = belegnummer,
                ErstelltAm = erstelltAm
            };
        }

        public virtual bool VersuchePdfZuOeffnen(string? dateipfad)
        {
            // Der nullable Parameter erlaubt ein sauberes false, wenn noch kein Beleg erzeugt wurde.
            if (string.IsNullOrWhiteSpace(dateipfad) || !File.Exists(dateipfad))
            {
                return false;
            }

            try
            {
                // UseShellExecute öffnet die Datei mit dem unter Windows eingestellten Standard-PDF-Programm.
                Process.Start(new ProcessStartInfo
                {
                    FileName = dateipfad,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                // Ein Fehler beim Öffnen ändert den bereits gespeicherten Zahlungs- und PDF-Datensatz nicht.
                return false;
            }
        }

        private static Document ErzeugeDokument(
            ReservationOverview reservation,
            decimal paymentAmount,
            string belegnummer,
            DateTime erstelltAm)
        {
            // Die PDF-Gestaltung liegt in einer eigenen Methode, damit die Erzeugungslogik übersichtlich bleibt.
            var document = new Document();
            document.Info.Title = $"Reservierungs- und Zahlungsbeleg {belegnummer}";
            document.Info.Subject = "Simulierter Online-Zahlungsbeleg";
            document.Info.Author = "Grand Horizon Hotel";

            // ! ist hier sicher, weil MigraDoc den Standardstil garantiert bereitstellt.
            var normalStyle = document.Styles[StyleNames.Normal]!;
            normalStyle.Font.Name = "Arial";
            normalStyle.Font.Size = Unit.FromPoint(10);
            normalStyle.Font.Color = Dunkelgrau;

            var section = document.AddSection();
            section.PageSetup.PageFormat = PageFormat.A4;
            section.PageSetup.TopMargin = Unit.FromCentimeter(1.6);
            section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
            section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
            section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

            FuegeKopfbereichHinzu(section, belegnummer, erstelltAm);
            FuegeStatusHinzu(section, reservation.OutstandingAmount <= 0m);
            FuegeAbschnittsTitelHinzu(section, "Reservierungsdaten");

            var reservationTable = ErzeugeDatentabelle(section);
            FuegeDatenzeileHinzu(reservationTable, "Reservierungscode", reservation.ReservationCode);
            FuegeDatenzeileHinzu(reservationTable, "Gastname", reservation.Guest);
            FuegeDatenzeileHinzu(reservationTable, "Zimmer", reservation.Room);
            FuegeDatenzeileHinzu(reservationTable, "Zimmerkategorie", reservation.Type);
            FuegeDatenzeileHinzu(reservationTable, "Anreise", reservation.CheckInDate.ToString("dd.MM.yyyy", DeutscheKultur));
            FuegeDatenzeileHinzu(reservationTable, "Abreise", reservation.CheckOutDate.ToString("dd.MM.yyyy", DeutscheKultur));
            FuegeDatenzeileHinzu(reservationTable, "Nächte", reservation.Nights.ToString(DeutscheKultur));
            FuegeDatenzeileHinzu(reservationTable, "Gäste", FormatiereGaeste(reservation.Adults, reservation.Children));

            FuegeAbschnittsTitelHinzu(section, "Zahlungsübersicht");
            var paymentTable = ErzeugeDatentabelle(section);
            FuegeDatenzeileHinzu(paymentTable, "Gesamtbetrag", FormatiereGeld(reservation.TotalAmount));
            FuegeDatenzeileHinzu(paymentTable, "Bereits bezahlt", FormatiereGeld(reservation.AmountPaid));
            FuegeDatenzeileHinzu(paymentTable, "Diese Zahlung", FormatiereGeld(paymentAmount));
            FuegeDatenzeileHinzu(paymentTable, "Offener Restbetrag", FormatiereGeld(reservation.OutstandingAmount), true);
            FuegeDatenzeileHinzu(paymentTable, "Zahlungsart", "Online");
            FuegeDatenzeileHinzu(
                paymentTable,
                "Zahlungsstatus",
                reservation.OutstandingAmount <= 0m ? "Vollständig bezahlt" : "Teilweise bezahlt");

            var hinweis = section.AddParagraph();
            hinweis.Format.SpaceBefore = Unit.FromCentimeter(0.65);
            hinweis.Format.SpaceAfter = Unit.FromCentimeter(0.2);
            hinweis.Format.Alignment = ParagraphAlignment.Center;
            hinweis.Format.Font.Size = Unit.FromPoint(9);
            hinweis.Format.Font.Color = Sekundaergrau;
            hinweis.AddText("Bitte bringen Sie diesen Beleg bei Bedarf zur Rezeption mit.");

            var footer = section.Footers.Primary.AddParagraph();
            footer.Format.Alignment = ParagraphAlignment.Center;
            footer.Format.Font.Size = Unit.FromPoint(8);
            footer.Format.Font.Color = Sekundaergrau;
            footer.AddText("Demo-Zahlung - es fand keine reale Banktransaktion statt.");
            footer.AddLineBreak();
            footer.AddText("Grand Horizon Hotel | Reservierungs- und Zahlungsbeleg");

            return document;
        }

        private static void FuegeKopfbereichHinzu(
            Section section,
            string belegnummer,
            DateTime erstelltAm)
        {
            var table = section.AddTable();
            table.AddColumn(Unit.FromCentimeter(10.6));
            table.AddColumn(Unit.FromCentimeter(6.8));
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;

            var hotel = row.Cells[0].AddParagraph();
            hotel.Format.Font.Size = Unit.FromPoint(23);
            hotel.Format.Font.Bold = true;
            hotel.Format.Font.Color = HotelBlau;
            hotel.AddText("GRAND HORIZON HOTEL");

            var subtitle = row.Cells[0].AddParagraph();
            subtitle.Format.SpaceBefore = Unit.FromPoint(3);
            subtitle.Format.Font.Size = Unit.FromPoint(13);
            subtitle.Format.Font.Color = Sekundaergrau;
            subtitle.AddText("Reservierungs- und Zahlungsbeleg");

            var metadata = row.Cells[1].AddParagraph();
            metadata.Format.Alignment = ParagraphAlignment.Right;
            metadata.Format.Font.Size = Unit.FromPoint(9);
            metadata.Format.Font.Color = Sekundaergrau;
            var belegText = metadata.AddFormattedText("Belegnummer\n", TextFormat.Bold);
            belegText.Color = HotelBlau;
            metadata.AddText(belegnummer);
            metadata.AddLineBreak();
            metadata.AddLineBreak();
            metadata.AddFormattedText("Erstellt am\n", TextFormat.Bold);
            metadata.AddText(erstelltAm.ToString("dd.MM.yyyy, HH:mm 'Uhr'", DeutscheKultur));

            var trennlinie = section.AddParagraph();
            trennlinie.Format.SpaceBefore = Unit.FromCentimeter(0.35);
            trennlinie.Format.SpaceAfter = Unit.FromCentimeter(0.35);
            trennlinie.Format.Borders.Bottom.Width = Unit.FromPoint(2);
            trennlinie.Format.Borders.Bottom.Color = HotelBlau;
        }

        private static void FuegeStatusHinzu(Section section, bool isFullyPaid)
        {
            var table = section.AddTable();
            table.AddColumn(Unit.FromCentimeter(17.4));
            var row = table.AddRow();
            row.Height = Unit.FromCentimeter(1.25);
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Shading.Color = isFullyPaid
                ? Color.FromRgb(232, 245, 233)
                : Color.FromRgb(255, 244, 229);

            var cell = row.Cells[0];
            cell.Borders.Width = Unit.FromPoint(1);
            cell.Borders.Color = isFullyPaid ? Erfolgsgruen : Teilzahlungsorange;
            var status = cell.AddParagraph(isFullyPaid ? "VOLLSTÄNDIG BEZAHLT" : "TEILWEISE BEZAHLT");
            status.Format.Alignment = ParagraphAlignment.Center;
            status.Format.Font.Bold = true;
            status.Format.Font.Size = Unit.FromPoint(15);
            status.Format.Font.Color = isFullyPaid ? Erfolgsgruen : Teilzahlungsorange;
        }

        private static void FuegeAbschnittsTitelHinzu(Section section, string titel)
        {
            var paragraph = section.AddParagraph();
            paragraph.Format.SpaceBefore = Unit.FromCentimeter(0.65);
            paragraph.Format.SpaceAfter = Unit.FromCentimeter(0.22);
            paragraph.Format.Font.Size = Unit.FromPoint(13);
            paragraph.Format.Font.Bold = true;
            paragraph.Format.Font.Color = HotelBlau;
            paragraph.AddText(titel);
        }

        private static Table ErzeugeDatentabelle(Section section)
        {
            var table = section.AddTable();
            table.AddColumn(Unit.FromCentimeter(6.2));
            table.AddColumn(Unit.FromCentimeter(11.2));
            table.Borders.Color = RahmenBlau;
            table.Borders.Width = Unit.FromPoint(0.6);
            table.Rows.VerticalAlignment = VerticalAlignment.Center;
            return table;
        }

        private static void FuegeDatenzeileHinzu(
            Table table,
            string bezeichnung,
            string wert,
            bool hervorheben = false)
        {
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(5);
            row.BottomPadding = Unit.FromPoint(5);
            row.Cells[0].Shading.Color = HellesBlau;
            row.Cells[0].Format.Font.Bold = true;
            row.Cells[0].Format.Font.Color = HotelBlau;
            row.Cells[0].AddParagraph(bezeichnung);

            if (hervorheben)
            {
                row.Cells[1].Shading.Color = Color.FromRgb(247, 250, 253);
                row.Cells[1].Format.Font.Bold = true;
                row.Cells[1].Format.Font.Size = Unit.FromPoint(11);
            }

            row.Cells[1].AddParagraph(wert);
        }

        private static string ErzeugeBelegnummer(DateTime erstelltAm)
        {
            return $"GH-{erstelltAm:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        }

        private static string BereinigeDateinamenTeil(string value)
        {
            var cleaned = new string(value
                .Trim()
                .ToUpperInvariant()
                .Select(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9'
                    ? character
                    : '_')
                .Take(40)
                .ToArray())
                .Trim('_');
            return string.IsNullOrWhiteSpace(cleaned) ? "RESERVIERUNG" : cleaned;
        }

        private static string FormatiereGeld(decimal amount)
        {
            return $"{amount.ToString("N2", DeutscheKultur)} €";
        }

        private static string FormatiereGaeste(int erwachsene, int kinder)
        {
            var erwachseneText = erwachsene == 1 ? "1 Erwachsener" : $"{erwachsene} Erwachsene";
            var kinderText = kinder == 1 ? "1 Kind" : $"{kinder} Kinder";
            return $"{erwachseneText}, {kinderText}";
        }
    }
}
