using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    // Die festen Restaurant-Anlässe werden als Objekte bereitgestellt, damit die ComboBox sauber binden kann.
    public class Anlass
    {
        public string Bezeichnung { get; set; }
        public string AnzeigeText => Bezeichnung;
    }
}
