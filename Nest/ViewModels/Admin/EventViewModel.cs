using Caliburn.Micro;
using Nest.Data;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Nest.ViewModels.Admin
{
    // This ViewModel is responsible for managing the events in the admin panel.
    public class EventViewModel : Screen
    {
        // The EventRepository is used to interact with the database for event-related operations.
        private readonly EventRepository _eventRepository;

        // Private fields to hold the values of the event properties.
        private string _title;
        private string _description;
        private DateTime _eventDate = DateTime.Today;
        private string _eventTimeText = "19:00";
        private string _location = "Hotel Lobby";
        private string _ticketPriceText = "0,00";
        private string _maxTicketsText = "100";
        private string _status = "Aktiv";

        private Event _selectedEvent;

        // ObservableCollection to hold the list of events for data binding in the UI.
        public ObservableCollection<Event> Events { get; set; }

        // Properties for data binding in the UI. Each property notifies the UI when its value changes.
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                NotifyOfPropertyChange(() => Title);
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                NotifyOfPropertyChange(() => Description);
            }
        }

        public DateTime EventDate
        {
            get => _eventDate;
            set
            {
                _eventDate = value;
                NotifyOfPropertyChange(() => EventDate);
            }
        }

        public string EventTimeText
        {
            get => _eventTimeText;
            set
            {
                _eventTimeText = value;
                NotifyOfPropertyChange(() => EventTimeText);
            }
        }

        public string Location
        {
            get => _location;
            set
            {
                _location = value;
                NotifyOfPropertyChange(() => Location);
            }
        }

        public string TicketPriceText
        {
            get => _ticketPriceText;
            set
            {
                _ticketPriceText = value;
                NotifyOfPropertyChange(() => TicketPriceText);
            }
        }

        public string MaxTicketsText
        {
            get => _maxTicketsText;
            set
            {
                _maxTicketsText = value;
                NotifyOfPropertyChange(() => MaxTicketsText);
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                NotifyOfPropertyChange(() => Status);
                NotifyOfPropertyChange(() => SelectedStatusIndex);
            }
        }

        // The SelectedEvent property is used to bind the selected event from the UI. When an event is selected, its details are populated into the form fields.
        public Event SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                _selectedEvent = value;
                NotifyOfPropertyChange(() => SelectedEvent);

                if (value != null)
                {
                    Title = value.Title;
                    Description = value.Description;
                    EventDate = value.EventDate;
                    EventTimeText = value.EventTime.ToString(@"hh\:mm");
                    Location = value.Location;
                    TicketPriceText = value.TicketPrice.ToString(
                        "F2",
                        CultureInfo.GetCultureInfo("de-DE"));
                    MaxTicketsText = value.MaxTickets.ToString();
                    Status = value.Status;
                }
            }
        }

        public int SelectedStatusIndex
        {
            get => Status switch
            {
                "Aktiv" => 0,
                "Inaktiv" => 1,
                "Ausgebucht" => 2,
                _ => 0
            };

            set
            {
                Status = value switch
                {
                    0 => "Aktiv",
                    1 => "Inaktiv",
                    2 => "Ausgebucht",
                    _ => "Aktiv"
                };

                NotifyOfPropertyChange(() => SelectedStatusIndex);
            }
        }

        // Constructor initializes the EventViewModel, sets up the EventRepository, and loads the list of events from the database. It also clears the form fields to their default values.
        public EventViewModel()
        {
            _eventRepository = new EventRepository();

            Events = new ObservableCollection<Event>(
                _eventRepository.GetAll()
            );

            ClearForm();
        }

        // This method retrieves the ticket preis from the TicketPriceText property. It attempts to parse the text into a decimal value using German culture formatting. If parsing fails, it returns 0.
        private decimal GetTicketPrice()
        {
            if (decimal.TryParse(
                TicketPriceText,
                NumberStyles.Number,
                CultureInfo.GetCultureInfo("de-DE"),
                out decimal preis))
            {
                return preis;
            }

            return 0;
        }

        public void PreisUp()
        {
            decimal preis = GetTicketPrice();

            if (preis < 9999.99m)
            {
                preis += 1.00m;

                if (preis > 9999.99m)
                    preis = 9999.99m;

                TicketPriceText = preis.ToString(
                    "F2",
                    CultureInfo.GetCultureInfo("de-DE"));
            }
        }

        public void PreisDown()
        {
            decimal preis = GetTicketPrice();

            if (preis > 0)
            {
                preis -= 1.00m;

                if (preis < 0)
                    preis = 0;

                TicketPriceText = preis.ToString(
                    "F2",
                    CultureInfo.GetCultureInfo("de-DE"));
            }
        }

        // This method retrieves the maximum number of tickets from the MaxTicketsText property. If the text cannot be parsed into an integer, it returns 0.
        private int GetMaxTickets()
        {
            if (int.TryParse(MaxTicketsText, out int tickets))
            {
                return tickets;
            }

            return 0;
        }

        public void MaxUp()
        {
            int tickets = GetMaxTickets();

            if (tickets < 9999)
            {
                tickets++;
                MaxTicketsText = tickets.ToString();
            }
        }

        public void MinDown()
        {
            int tickets = GetMaxTickets();

            if (tickets > 0)
            {
                tickets--;
                MaxTicketsText = tickets.ToString();
            }
        }

        // This method retrieves the event time from the EventTimeText property. It attempts to parse the text into a TimeSpan value. If parsing fails, it returns a default time of 19:00 (7:00 PM).
        private TimeSpan GetEventTime()
        {
            if (TimeSpan.TryParse(EventTimeText, out TimeSpan time))
            {
                return time;
            }

            return new TimeSpan(19, 0, 0);
        }

        public void PersonUp()
        {
            TimeSpan time = GetEventTime();

            time = time.Add(TimeSpan.FromHours(1));

            if (time.TotalHours >= 24)
                time = new TimeSpan(23, 0, 0);

            EventTimeText = time.ToString(@"hh\:mm");
        }

        public void PersonDown()
        {
            TimeSpan time = GetEventTime();

            time = time.Subtract(TimeSpan.FromHours(1));

            if (time.TotalHours < 0)
                time = new TimeSpan(0, 0, 0);

            EventTimeText = time.ToString(@"hh\:mm");
        }

        public void ClearForm()
        {
            SelectedEvent = null;

            Title = "";
            Description = "";
            EventDate = DateTime.Today;
            EventTimeText = "19:00";
            Location = "Hotel Lobby";
            TicketPriceText = "0,00";
            MaxTicketsText = "100";
            Status = "Aktiv";
        }

        // This method is responsible for adding a new event. It first checks if the required fields (Title and Location) are filled. If not, it shows a warning message. Then, it retrieves the values from the form fields, creates a new Event object, saves it to the database, adds it to the Events collection, shows a success message, and finally clears the form for new input.
        public void Hinzufügen()
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                MessageBox.Show(
                    "Bitte geben Sie einen Titel ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(Location))
            {
                MessageBox.Show(
                    "Bitte geben Sie einen Ort ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Datum darf nicht in der Vergangenheit liegen
            if (EventDate.Date < DateTime.Today)
            {
                MessageBox.Show(
                    "Das Veranstaltungsdatum darf nicht in der Vergangenheit liegen.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Max. Tickets prüfen
            int maxTickets = GetMaxTickets();

            if (maxTickets <= 0)
            {
                MessageBox.Show(
                    "Die Anzahl der Tickets muss größer als 0 sein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            TimeSpan eventTime = GetEventTime();

            // Zeit prüfen
            if (!TimeSpan.TryParse(EventTimeText, out eventTime))
            {
                MessageBox.Show(
                    "Bitte geben Sie eine gültige Uhrzeit ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Doppelte Veranstaltung prüfen
            if (IsDuplicateEvent())
            {
                MessageBox.Show(
                    "Diese Veranstaltung existiert bereits.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            decimal ticketPrice = GetTicketPrice();

            var newEvent = new Event
            {
                Title = Title.Trim(),
                Description = Description?.Trim(),
                EventDate = EventDate,
                EventTime = eventTime,
                Location = Location.Trim(),
                TicketPrice = ticketPrice,
                MaxTickets = maxTickets,
                SoldTickets = 0,
                ImageUrl = null,
                Status = Status,
                CreatedBy = null
            };

            _eventRepository.Add(newEvent);

            // Liste aus der Datenbank neu laden
            Events.Clear();

            foreach (var eventItem in _eventRepository.GetAll())
            {
                Events.Add(eventItem);
            }

            MessageBox.Show(
                "Die Veranstaltung wurde erfolgreich hinzugefügt.",
                "Erfolgreich",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // This method is responsible for updating an existing event. It first checks if an event is selected and if the required fields (Title and Location) are filled. If not, it shows a warning message. Then, it retrieves the values from the form fields, updates the properties of the selected event, saves the changes to the database, updates the Events collection, shows a success message, and finally clears the form.
        public void Aktualisieren()
        {
            if (SelectedEvent == null)
            {
                MessageBox.Show(
                    "Bitte wählen Sie zuerst eine Veranstaltung aus.",
                    "Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(Title))
            {
                MessageBox.Show(
                    "Bitte geben Sie einen Titel ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(Location))
            {
                MessageBox.Show(
                    "Bitte geben Sie einen Ort ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (EventDate.Date < DateTime.Today)
            {
                MessageBox.Show(
                    "Das Veranstaltungsdatum darf nicht in der Vergangenheit liegen.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            int maxTickets = GetMaxTickets();

            if (maxTickets <= 0)
            {
                MessageBox.Show(
                    "Die Anzahl der Tickets muss größer als 0 sein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (maxTickets < SelectedEvent.SoldTickets)
            {
                MessageBox.Show(
                    $"Die maximale Ticketanzahl darf nicht kleiner als die bereits verkauften Tickets ({SelectedEvent.SoldTickets}) sein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!TimeSpan.TryParse(EventTimeText, out TimeSpan eventTime))
            {
                MessageBox.Show(
                    "Bitte geben Sie eine gültige Uhrzeit ein.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // Prüfen, ob dadurch ein Duplikat entsteht
            if (IsDuplicateEvent(SelectedEvent.Id))
            {
                MessageBox.Show(
                    "Diese Veranstaltung existiert bereits.",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            decimal ticketPrice = GetTicketPrice();

            // Neues Objekt für die aktualisierten Daten
            var updatedEvent = new Event
            {
                Id = SelectedEvent.Id,
                Title = Title.Trim(),
                Description = Description?.Trim(),
                EventDate = EventDate,
                EventTime = eventTime,
                Location = Location.Trim(),
                TicketPrice = ticketPrice,
                MaxTickets = maxTickets,
                SoldTickets = SelectedEvent.SoldTickets,
                ImageUrl = SelectedEvent.ImageUrl,
                Status = Status,
                CreatedBy = SelectedEvent.CreatedBy,
                DateCreated = SelectedEvent.DateCreated
            };

            _eventRepository.Update(updatedEvent);

            // UI aktualisieren
            int index = Events.IndexOf(SelectedEvent);

            if (index >= 0)
            {
                Events[index] = updatedEvent;
            }

            MessageBox.Show(
                "Die Veranstaltung wurde erfolgreich aktualisiert.",
                "Erfolgreich",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // This method is responsible for deleting an existing event. It first checks if an event is selected. If not, it shows a warning message. Then, it asks for confirmation from the user before proceeding with the deletion. If confirmed, it deletes the event from the database, removes it from the Events collection, shows a success message, and finally clears the form.
        public void Löschen()
        {
            if (SelectedEvent == null)
            {
                MessageBox.Show(
                    "Bitte wählen Sie zuerst eine Veranstaltung aus.",
                    "Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"Möchten Sie die Veranstaltung \"{SelectedEvent.Title}\" wirklich löschen?",
                "Veranstaltung löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Aus der Datenbank löschen
            _eventRepository.Delete(SelectedEvent.Id);

            // Aus der Anzeige entfernen
            Events.Remove(SelectedEvent);

            MessageBox.Show(
                "Die Veranstaltung wurde erfolgreich gelöscht.",
                "Erfolgreich",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private bool IsDuplicateEvent(int? excludeId = null)
        {
            string titel = Title?.Trim();

            TimeSpan eventTime = GetEventTime();

            return Events.Any(e =>
                (!excludeId.HasValue || e.Id != excludeId.Value) &&
                e.Title.Equals(titel, StringComparison.OrdinalIgnoreCase) &&
                e.EventDate.Date == EventDate.Date &&
                e.EventTime == eventTime &&
                e.Location.Equals(Location?.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
