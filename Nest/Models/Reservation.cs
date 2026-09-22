namespace Nest.Models
{
    // Das Model bündelt alle geprüften Zimmerbuchungsdaten für den atomaren Speichervorgang im Repository.
    public class Reservation
    {
        public int Id { get; set; }
        public int GuestId { get; set; }
        public int RoomId { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int Nights { get; set; }
        public int Adults { get; set; }
        public int Children { get; set; }
        // decimal vermeidet ungeeignete binäre Rundungsabweichungen bei Geldbeträgen.
        public decimal TotalAmount { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; }
        public string Status { get; set; }
        public string ReservationCode { get; set; }
        public DateTime DateCreated { get; set; }
        public string SpecialRequests { get; set; }
        // Im Kundenportal gibt es keinen angemeldeten Rezeptionisten; deshalb ist diese Zuordnung optional.
        public int? ReceptionistId { get; set; }
    }
}
