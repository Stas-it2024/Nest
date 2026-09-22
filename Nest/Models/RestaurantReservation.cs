namespace Nest.Models
{
    // Dieses reine Datenobjekt kann unabhängig von Oberfläche und Datenbanklogik weitergegeben werden.
    public class RestaurantReservation
    {
        public int Id { get; set; }
        public int? ReservationId { get; set; }
        public string GuestName { get; set; } = string.Empty;
        public DateTime ReservationDate { get; set; }
        public TimeSpan ReservationTime { get; set; }
        public int GuestCount { get; set; }
        public string Occasion { get; set; } = string.Empty;
        // Sonderwünsche sind freiwillig; ? macht diese Eigenschaft bewusst nullable.
        public string? SpecialRequests { get; set; }
        public string Status { get; set; } = "Angefragt";
    }
}
