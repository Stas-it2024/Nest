using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Models
{
    public class Event
    {
        public int Id { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public DateTime EventDate { get; set; }

        public TimeSpan EventTime { get; set; }

        public string Location { get; set; }

        public decimal TicketPrice { get; set; }

        public int MaxTickets { get; set; }

        public int SoldTickets { get; set; }

        public string ImageUrl { get; set; }

        public string Status { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime DateCreated { get; set; }

        // This property calculates the number of available tickets based on the maximum tickets and sold tickets.
        public int AvailableTickets
        {
            get
            {
                return MaxTickets - SoldTickets;
            }
        }
    }
}
