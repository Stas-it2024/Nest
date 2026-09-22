using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    // Das Model bildet einen Zimmerdatensatz ab und bleibt frei von Datenbankzugriff und Benutzerinteraktion.
    public class Room
    {
        public int Id { get; set; }
        public int RoomNumber { get; set; }
        public string Type { get; set; }
        public decimal PricePerNight { get; set; }
        public int Capacity { get; set; }
        public string Status { get; set; }
        public string? Description { get; set; }
        public string? ImagePath { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime? DateDelete { get; set; }

        // Die abgeleitete Anzeige kombiniert die wichtigsten Auswahlwerte, ohne redundante Daten zu speichern.
        public string DisplayText => $"{RoomNumber} - {Type} (€{PricePerNight:F2}/Nacht)";
    }
}

