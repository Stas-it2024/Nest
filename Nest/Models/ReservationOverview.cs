using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    // Dieses Anzeige-Model enthält genau die zusammengeführten Daten, die Zahlung und Übersicht benötigen.
    public class ReservationOverview
    {
        public string ReservationCode { get; set; }
        public string Guest { get; set; }
        public string Room { get; set; }
        public string Type { get; set; }

        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int Nights { get; set; }
        public int Adults { get; set; }
        public int Children { get; set; }

        public decimal TotalAmount { get; set; }
        public decimal AmountPaid { get; set; }
        // Der offene Betrag wird immer aktuell berechnet und kann durch Math.Max nie negativ angezeigt werden.
        public decimal OutstandingAmount => Math.Max(0m, TotalAmount - AmountPaid);

        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
