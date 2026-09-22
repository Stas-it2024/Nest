namespace Nest.Models
{
    // Das Model beschreibt nur die Daten einer Taxianfrage und hält die Schichten des MVVM-Aufbaus getrennt.
    public class TaxiOrder
    {
        public int Id { get; set; }
        // Die Verknüpfung mit einer Zimmerreservierung ist optional und darf deshalb null sein.
        public int? ReservationId { get; set; }
        public string GuestName { get; set; } = string.Empty;
        public string? GuestPhone { get; set; }
        public string PickupLocation { get; set; } = string.Empty;
        public DateTime PickupDate { get; set; }
        public TimeSpan PickupTime { get; set; }
        public string VehicleType { get; set; } = string.Empty;
        public string Status { get; set; } = "Angefragt";
    }
}
