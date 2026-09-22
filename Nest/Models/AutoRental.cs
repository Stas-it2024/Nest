namespace Nest.Models
{
    // Das Model transportiert geprüfte Mietdaten zwischen ViewModel und Repository, ohne UI- oder SQL-Logik zu enthalten.
    public class AutoRental
    {
        public int Id { get; set; }
        // Durch ? darf eine Mietanfrage auch ohne zugehörige Zimmerreservierung gespeichert werden.
        public int? ReservationId { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string VehicleCategory { get; set; } = string.Empty;
        public DateTime PickupDate { get; set; }
        public DateTime ReturnDate { get; set; }
        // decimal wird für Geld verwendet, damit Preise dezimal genau berechnet werden.
        public decimal DailyPrice { get; set; }
        public decimal TotalPrice { get; set; }
        public string Status { get; set; } = "Angefragt";
    }
}
