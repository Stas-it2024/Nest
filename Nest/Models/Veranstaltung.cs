namespace Nest.Models
{
    // Das Kunden-Model ergänzt die Datenbankwerte um fertige Anzeigewerte für das Data Binding.
    public class Veranstaltung
    {
        public int Id { get; set; }
        public string Titel { get; set; } = string.Empty;
        public string Beschreibung { get; set; } = string.Empty;
        public DateTime Datum { get; set; }
        public TimeSpan Uhrzeit { get; set; }
        public string Ort { get; set; } = string.Empty;
        public decimal TicketPreis { get; set; }
        public int MaximaleTickets { get; set; }
        public int VerkaufteTickets { get; set; }
        public string? BildUrl { get; set; }

        // Diese berechneten Eigenschaften vermeiden Formatierungs- und Rechenlogik direkt in der View.
        public DateTime Startzeit => Datum.Date + Uhrzeit;
        public int VerfuegbareTickets => Math.Max(0, MaximaleTickets - VerkaufteTickets);
        public string DatumText => Datum.ToString("dd.MM.yyyy");
        public string UhrzeitText => $"{Uhrzeit:hh\\:mm} Uhr";
        public string PreisText => TicketPreis == 0m ? "Kostenlos" : $"{TicketPreis:F2} € pro Ticket";
    }
}
