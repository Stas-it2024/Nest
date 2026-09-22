using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    // Dieses Model transportiert Gastdaten zwischen Buchungslogik und Gast-Repository.
    public class Guest
    {
        public int Id { get; set; }
        public string FirstName { get; set; }      //  Vorname (Pflichtfeld *)
        public string LastName { get; set; }       //  Nachname (Pflichtfeld *)
        public string Email { get; set; }          //  E-Mail
        public string Phone { get; set; }          //  Telefon
        public string IdType { get; set; }         //  Ausweistyp (Reisepass, Personalausweis, etc.)
        public string IdNumber { get; set; }       //  Ausweisnummer
        public string Nationality { get; set; }    //  Nationalität
        public DateTime CreatedAt { get; set; } //  Erstellungsdatum
        public DateTime? DateAdded { get; set; }

        // Der vollständige Name wird aus den Quelldaten berechnet und muss deshalb nicht separat gespeichert werden.
        public string FullName => $"{FirstName} {LastName}";
    }
}
