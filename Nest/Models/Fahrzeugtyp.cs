using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    // Dieses Auswahl-Model liefert der Taxi-ComboBox einen klaren Anzeige- und Speicherwert.
    public class Fahrzeugtyp
    {
        public string Bezeichnung { get; set; }
        public string AnzeigeText => Bezeichnung;
    }
}
