using Caliburn.Micro;
using Nest.Data;
using Nest.ViewModels.Shell;

namespace Nest.ViewModels.CustomerPortal
{
    // Dieses ViewModel setzt die einzelnen Kundenfunktionen zu einem gemeinsamen Portal zusammen.
    public class CustomerPortalViewModel : Screen
    {
        private readonly ShellViewModel _shell;

        public ZimmerBuchungViewModel ZimmerBuchung { get; set; }
        public AutoMietenViewModel AutoMieten { get; set; }
        public TaxiBestellenViewModel TaxiBestellen { get; set; }
        public RestaurantReservierungViewModel RestaurantReservierung { get; set; }
        public VeranstaltungenViewModel Veranstaltungen { get; set; }
        public OnlineZahlungViewModel OnlineZahlung { get; set; }

        public string Begruessung { get; }
        public bool ShowPortalHeader { get; }

        public CustomerPortalViewModel(ShellViewModel shell, string? gastname, bool isEmbeddedInReception = false)
        {
            _shell = shell;
            ShowPortalHeader = !isEmbeddedInReception;
            // Nullable erlaubt den Portalaufruf auch ohne Namen; Trim entfernt äußere Leerzeichen.
            var bereinigterGastname = gastname?.Trim();
            Begruessung = string.IsNullOrWhiteSpace(bereinigterGastname)
                ? "Willkommen - Bitte wählen Sie einen Service"
                : $"Willkommen {bereinigterGastname} - Bitte wählen Sie einen Service";

            // Jedes Repository kapselt den Datenbankzugriff für seinen Fachbereich.
            var roomRepo = new RoomRepository();
            var guestRepo = new GastRepository();
            var resRepo = new ReservationRepository();
            var eventRepo = new EventRepository();
            var autoRentalRepo = new AutoRentalRepository();
            var taxiOrderRepo = new TaxiOrderRepository();
            var restaurantReservationRepo = new RestaurantReservationRepository();

            // Die Übergabe über Konstruktoren macht die Abhängigkeiten der ViewModels sichtbar
            // und ermöglicht später einen leichteren Austausch, zum Beispiel in Tests.
            ZimmerBuchung = new ZimmerBuchungViewModel(roomRepo, guestRepo, resRepo);

            AutoMieten = new AutoMietenViewModel(autoRentalRepo);
            TaxiBestellen = new TaxiBestellenViewModel(taxiOrderRepo);
            RestaurantReservierung = new RestaurantReservierungViewModel(restaurantReservationRepo);
            Veranstaltungen = new VeranstaltungenViewModel(eventRepo, bereinigterGastname);
            OnlineZahlung = new OnlineZahlungViewModel(resRepo);
        }

        public void Abmelden()
        {
            // Die Shell steuert den Ansichtswechsel zurück zum Login zentral.
            _shell.WindowTitle = "Hotel Management System - Login";
            _shell.ShowLogin();
        }
    }
}
