using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    public class Service
    {
        public int Id { get; set; }

        public string ServiceName { get; set; }

        public string Category { get; set; }

        public decimal Price { get; set; }

        public string Status { get; set; }

        public DateTime DateAdded { get; set; }

        public DateTime? DateDelete { get; set; }

        public string CategoryDisplay
        {
            get
            {
                return Category switch
                {
                    "Speisen und Getraenke" => "Speisen und Getränke",
                    "Waesche" => "Wäsche",
                    "Wellness" => "Wellness",
                    "Transport" => "Transport",
                    "Sonstiges" => "Sonstiges",
                    _ => Category
                };
            }
        }
    }
}
