using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    // Ein kleines Auswahl-Model hält Kategorie und Preis zusammen, damit die ComboBox keine getrennten Listen braucht.
    public class Fahrzeugkategorie
    {
        public string Name { get; set; }
        public decimal Tagespreis { get; set; }
        // Der berechnete Text zeigt dem Kunden Auswahl und Preis, ohne einen zusätzlichen Wert speichern zu müssen.
        public string AnzeigeText => $"{Name} - €{Tagespreis:F2}/Tag";
    }
}
