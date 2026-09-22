using MySql.Data.MySqlClient;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Data;

namespace Nest.Data
{
    public enum TicketBookingStatus
    {
        Success,
        EventNotFound,
        EventInPast,
        InvalidQuantity,
        InsufficientTickets,
        MissingGuestName,
        MissingGuestEmail,
        MissingGuestPhone
    }

    public sealed class TicketBookingRequest
    {
        public int EventId { get; init; }
        public string GuestName { get; init; } = string.Empty;
        public string GuestEmail { get; init; } = string.Empty;
        public string GuestPhone { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public int? ReservationId { get; init; }
    }

    public sealed class TicketBookingResult
    {
        public TicketBookingStatus Status { get; init; }
        public string TicketCode { get; init; } = string.Empty;
        public decimal UnitPrice { get; init; }
        public decimal TotalPrice { get; init; }
        public int RemainingTickets { get; init; }
    }

    public class EventRepository
    {
        // Das Kundenportal lädt nur heutige und zukünftige Veranstaltungen in zeitlicher Reihenfolge.
        private const string LoadEventsSql = @"
            SELECT
                id,
                titel,
                beschreibung,
                veranstaltungs_datum,
                veranstaltungs_zeit,
                ort,
                ticket_preis,
                maximale_tickets,
                verkaufte_tickets,
                bild_url
            FROM veranstaltungen
            WHERE veranstaltungs_datum >= CURDATE()
            ORDER BY veranstaltungs_datum, veranstaltungs_zeit;";

        public List<Event> GetAll()
        {
            var veranstaltungen = new List<Event>();

            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                SELECT id,
                       titel,
                       beschreibung,
                       veranstaltungs_datum,
                       veranstaltungs_zeit,
                       ort,
                       ticket_preis,
                       maximale_tickets,
                       verkaufte_tickets,
                       bild_url,
                       status,
                       erstellt_von,
                       erstellt_am
                FROM veranstaltungen
                ORDER BY veranstaltungs_datum, veranstaltungs_zeit";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                veranstaltungen.Add(new Event
                {
                    Id = reader.GetInt32("id"),
                    Title = reader.GetString("titel"),

                    Description = reader.IsDBNull(reader.GetOrdinal("beschreibung"))
                        ? null
                        : reader.GetString("beschreibung"),

                    EventDate = reader.GetDateTime("veranstaltungs_datum"),
                    EventTime = reader.GetTimeSpan("veranstaltungs_zeit"),

                    Location = reader.IsDBNull(reader.GetOrdinal("ort"))
                        ? null
                        : reader.GetString("ort"),

                    TicketPrice = reader.GetDecimal("ticket_preis"),
                    MaxTickets = reader.GetInt32("maximale_tickets"),
                    SoldTickets = reader.GetInt32("verkaufte_tickets"),

                    ImageUrl = reader.IsDBNull(reader.GetOrdinal("bild_url"))
                        ? null
                        : reader.GetString("bild_url"),

                    Status = reader.GetString("status"),

                    CreatedBy = reader.IsDBNull(reader.GetOrdinal("erstellt_von"))
                        ? null
                        : reader.GetInt32("erstellt_von"),

                    DateCreated = reader.GetDateTime("erstellt_am")
                });
            }

            return veranstaltungen;
        }

        public void Add(Event eventItem)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                INSERT INTO veranstaltungen
                (
                    titel,
                    beschreibung,
                    veranstaltungs_datum,
                    veranstaltungs_zeit,
                    ort,
                    ticket_preis,
                    maximale_tickets,
                    verkaufte_tickets,
                    bild_url,
                    status,
                    erstellt_von
                )
                VALUES
                (
                    @titel,
                    @beschreibung,
                    @veranstaltungs_datum,
                    @veranstaltungs_zeit,
                    @ort,
                    @ticket_preis,
                    @maximale_tickets,
                    @verkaufte_tickets,
                    @bild_url,
                    @status,
                    @erstellt_von
                )";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@titel", eventItem.Title);
            cmd.Parameters.AddWithValue("@beschreibung", eventItem.Description);
            cmd.Parameters.AddWithValue("@veranstaltungs_datum", eventItem.EventDate);
            cmd.Parameters.AddWithValue("@veranstaltungs_zeit", eventItem.EventTime);
            cmd.Parameters.AddWithValue("@ort", eventItem.Location);
            cmd.Parameters.AddWithValue("@ticket_preis", eventItem.TicketPrice);
            cmd.Parameters.AddWithValue("@maximale_tickets", eventItem.MaxTickets);
            cmd.Parameters.AddWithValue("@verkaufte_tickets", eventItem.SoldTickets);
            cmd.Parameters.AddWithValue("@bild_url", eventItem.ImageUrl);
            cmd.Parameters.AddWithValue("@status", eventItem.Status);
            cmd.Parameters.AddWithValue("@erstellt_von", eventItem.CreatedBy);

            cmd.ExecuteNonQuery();

            eventItem.Id = (int)cmd.LastInsertedId;
        }

        public void Update(Event eventItem)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                UPDATE veranstaltungen
                SET titel = @titel,
                    beschreibung = @beschreibung,
                    veranstaltungs_datum = @veranstaltungs_datum,
                    veranstaltungs_zeit = @veranstaltungs_zeit,
                    ort = @ort,
                    ticket_preis = @ticket_preis,
                    maximale_tickets = @maximale_tickets,
                    verkaufte_tickets = @verkaufte_tickets,
                    bild_url = @bild_url,
                    status = @status,
                    erstellt_von = @erstellt_von
                WHERE id = @id";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", eventItem.Id);
            cmd.Parameters.AddWithValue("@titel", eventItem.Title);
            cmd.Parameters.AddWithValue("@beschreibung", eventItem.Description);
            cmd.Parameters.AddWithValue("@veranstaltungs_datum", eventItem.EventDate);
            cmd.Parameters.AddWithValue("@veranstaltungs_zeit", eventItem.EventTime);
            cmd.Parameters.AddWithValue("@ort", eventItem.Location);
            cmd.Parameters.AddWithValue("@ticket_preis", eventItem.TicketPrice);
            cmd.Parameters.AddWithValue("@maximale_tickets", eventItem.MaxTickets);
            cmd.Parameters.AddWithValue("@verkaufte_tickets", eventItem.SoldTickets);
            cmd.Parameters.AddWithValue("@bild_url", eventItem.ImageUrl);
            cmd.Parameters.AddWithValue("@status", eventItem.Status);
            cmd.Parameters.AddWithValue("@erstellt_von", eventItem.CreatedBy);

            cmd.ExecuteNonQuery();
        }

        public void Delete(int eventId)
        {
            using var conn = DB.GetConnection();
            conn.Open();

            string sql = @"
                DELETE FROM veranstaltungen
                WHERE id = @id";

            using var cmd = new MySqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@id", eventId);

            cmd.ExecuteNonQuery();
        }

        public List<Veranstaltung> GetUpcomingEvents()
        {
            var veranstaltungen = new List<Veranstaltung>();

            // using var gibt Verbindung, Befehl und Reader auch bei einem Fehler automatisch frei.
            using var connection = DB.GetConnection();
            connection.Open();

            using var command = new MySqlCommand(
                LoadEventsSql,
                connection);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                veranstaltungen.Add(MapEvent(reader));
            }

            return veranstaltungen;
        }

        public TicketBookingResult BookTickets(
            TicketBookingRequest request)
        {
            // Offensichtlich ungültige Kundendaten werden ohne unnötigen Datenbankzugriff abgewiesen.
            var basicValidationResult = ValidateRequest(request);

            if (basicValidationResult != null)
            {
                return basicValidationResult;
            }

            using var connection = DB.GetConnection();
            connection.Open();

            // Bestellung und Ticketbestand müssen gemeinsam erfolgreich sein oder gemeinsam zurückgenommen werden.
            using var transaction =
                connection.BeginTransaction(IsolationLevel.ReadCommitted);

            try
            {
                // FOR UPDATE sperrt das Event, damit parallele Kunden nicht dieselben Resttickets kaufen.
                const string lockEventSql = @"
                    SELECT
                        veranstaltungs_datum,
                        veranstaltungs_zeit,
                        ticket_preis,
                        maximale_tickets,
                        verkaufte_tickets
                    FROM veranstaltungen
                    WHERE id = @EventId
                    FOR UPDATE;";

                var eventFound = false;
                var eventDate = default(DateTime);
                var eventTime = default(TimeSpan);
                var unitPrice = 0m;
                var maximumTickets = 0;
                var soldTickets = 0;

                using (var lockCommand =
                    new MySqlCommand(
                        lockEventSql,
                        connection,
                        transaction))
                {
                    lockCommand.Parameters
                        .Add("@EventId", MySqlDbType.Int32)
                        .Value = request.EventId;

                    using var reader =
                        lockCommand.ExecuteReader();

                    eventFound = reader.Read();

                    if (eventFound)
                    {
                        eventDate =
                            reader.GetDateTime("veranstaltungs_datum").Date;

                        eventTime =
                            reader.IsDBNull(
                                reader.GetOrdinal("veranstaltungs_zeit"))
                            ? TimeSpan.Zero
                            : reader.GetTimeSpan(
                                reader.GetOrdinal("veranstaltungs_zeit"));

                        unitPrice =
                            reader.GetDecimal("ticket_preis");

                        maximumTickets =
                            reader.IsDBNull(
                                reader.GetOrdinal("maximale_tickets"))
                            ? 0
                            : reader.GetInt32("maximale_tickets");

                        soldTickets =
                            reader.IsDBNull(
                                reader.GetOrdinal("verkaufte_tickets"))
                            ? 0
                            : reader.GetInt32("verkaufte_tickets");
                    }
                }

                if (!eventFound)
                {
                    transaction.Rollback();
                    return Failed(
                        TicketBookingStatus.EventNotFound);
                }

                if (eventDate + eventTime < DateTime.Now)
                {
                    transaction.Rollback();
                    return Failed(
                        TicketBookingStatus.EventInPast);
                }

                // Die Verfügbarkeit wird unter der Sperre erneut geprüft und ist damit aktueller als die Anzeige.
                if ((long)soldTickets + request.Quantity
                    > maximumTickets)
                {
                    transaction.Rollback();

                    return new TicketBookingResult
                    {
                        Status =
                            TicketBookingStatus.InsufficientTickets,

                        RemainingTickets =
                            Math.Max(
                                0,
                                maximumTickets - soldTickets)
                    };
                }

                // decimal ist für Geld geeignet, weil Dezimalwerte ohne typische binäre Rundungsfehler berechnet werden.
                var totalPrice =
                    request.Quantity * unitPrice;

                var ticketCode =
                    GenerateTicketCode();

                var orderedAt = DateTime.Now;

                // Zuerst wird die konkrete Ticketbestellung innerhalb derselben Transaktion gespeichert.
                const string insertOrderSql = @"
                    INSERT INTO ticketbestellungen
                        (
                            veranstaltung_id,
                            gast_name,
                            gast_email,
                            gast_telefon,
                            anzahl,
                            einzelpreis,
                            gesamtpreis,
                            ticket_nummer,
                            reservierung_id,
                            bestellt_am
                        )
                    VALUES
                        (
                            @EventId,
                            @GuestName,
                            @GuestEmail,
                            @GuestPhone,
                            @Quantity,
                            @UnitPrice,
                            @TotalPrice,
                            @TicketCode,
                            @ReservationId,
                            @DateOrdered
                        );";

                using (var insertCommand =
                    new MySqlCommand(
                        insertOrderSql,
                        connection,
                        transaction))
                {
                    // Parameter trennen alle Kundeneingaben vom SQL-Text und schützen vor SQL-Injection.
                    insertCommand.Parameters
                        .Add("@EventId", MySqlDbType.Int32)
                        .Value = request.EventId;

                    insertCommand.Parameters
                        .Add("@GuestName", MySqlDbType.VarChar)
                        .Value = request.GuestName.Trim();

                    insertCommand.Parameters
                        .Add("@GuestEmail", MySqlDbType.VarChar)
                        .Value = request.GuestEmail.Trim();

                    insertCommand.Parameters
                        .Add("@GuestPhone", MySqlDbType.VarChar)
                        .Value = request.GuestPhone.Trim();

                    insertCommand.Parameters
                        .Add("@Quantity", MySqlDbType.Int32)
                        .Value = request.Quantity;

                    insertCommand.Parameters
                        .Add("@UnitPrice", MySqlDbType.Decimal)
                        .Value = unitPrice;

                    insertCommand.Parameters
                        .Add("@TotalPrice", MySqlDbType.Decimal)
                        .Value = totalPrice;

                    insertCommand.Parameters
                        .Add("@TicketCode", MySqlDbType.VarChar)
                        .Value = ticketCode;

                    // null bedeutet, dass die Ticketbestellung nicht an eine Zimmerreservierung gebunden ist.
                    insertCommand.Parameters
                        .Add("@ReservationId", MySqlDbType.Int32)
                        .Value =
                            request.ReservationId.HasValue
                            ? request.ReservationId.Value
                            : DBNull.Value;

                    insertCommand.Parameters
                        .Add("@DateOrdered", MySqlDbType.DateTime)
                        .Value = orderedAt;

                    if (insertCommand.ExecuteNonQuery() != 1)
                    {
                        throw new InvalidOperationException(
                            "Die Ticketbestellung konnte nicht gespeichert werden.");
                    }
                }

                // Danach wird der verkaufte Bestand erhöht; beide Änderungen gehören fachlich zusammen.
                const string updateEventSql = @"
                    UPDATE veranstaltungen
                    SET verkaufte_tickets =
                        COALESCE(verkaufte_tickets, 0) + @Quantity
                    WHERE id = @EventId;";

                using (var updateCommand =
                    new MySqlCommand(
                        updateEventSql,
                        connection,
                        transaction))
                {
                    updateCommand.Parameters
                        .Add("@Quantity", MySqlDbType.Int32)
                        .Value = request.Quantity;

                    updateCommand.Parameters
                        .Add("@EventId", MySqlDbType.Int32)
                        .Value = request.EventId;

                    if (updateCommand.ExecuteNonQuery() != 1)
                    {
                        throw new InvalidOperationException(
                            "Der Ticketbestand konnte nicht aktualisiert werden.");
                    }
                }

                // Commit bestätigt beide Datenbankänderungen erst nach vollständig erfolgreicher Ausführung.
                transaction.Commit();

                return new TicketBookingResult
                {
                    Status = TicketBookingStatus.Success,
                    TicketCode = ticketCode,
                    UnitPrice = unitPrice,
                    TotalPrice = totalPrice,
                    RemainingTickets =
                        maximumTickets
                        - soldTickets
                        - request.Quantity
                };
            }
            catch
            {
                // Rollback verhindert eine Bestellung ohne Bestandsänderung oder umgekehrt.
                transaction.Rollback();
                throw;
            }
        }

        private static TicketBookingResult? ValidateRequest(
            TicketBookingRequest request)
        {
            if (request.Quantity <= 0)
            {
                return Failed(
                    TicketBookingStatus.InvalidQuantity);
            }

            if (string.IsNullOrWhiteSpace(request.GuestName))
            {
                return Failed(
                    TicketBookingStatus.MissingGuestName);
            }

            if (string.IsNullOrWhiteSpace(request.GuestEmail))
            {
                return Failed(
                    TicketBookingStatus.MissingGuestEmail);
            }

            return string.IsNullOrWhiteSpace(request.GuestPhone)
                ? Failed(TicketBookingStatus.MissingGuestPhone)
                : null;
        }

        private static TicketBookingResult Failed(
            TicketBookingStatus status)
        {
            return new TicketBookingResult
            {
                Status = status
            };
        }

        private static string GenerateTicketCode()
        {
            return
                $"TKT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"
                [..25]
                .ToUpperInvariant();
        }

        private static Veranstaltung MapEvent(
            MySqlDataReader reader)
        {
            return new Veranstaltung
            {
                Id = reader.GetInt32("id"),

                Titel = reader.GetString("titel"),

                Beschreibung =
                    reader.IsDBNull(
                        reader.GetOrdinal("beschreibung"))
                    ? string.Empty
                    : reader.GetString("beschreibung"),

                Datum =
                    reader.GetDateTime("veranstaltungs_datum").Date,

                Uhrzeit =
                    reader.IsDBNull(
                        reader.GetOrdinal("veranstaltungs_zeit"))
                    ? TimeSpan.Zero
                    : reader.GetTimeSpan(
                        reader.GetOrdinal("veranstaltungs_zeit")),

                Ort =
                    reader.IsDBNull(
                        reader.GetOrdinal("ort"))
                    ? string.Empty
                    : reader.GetString("ort"),

                TicketPreis =
                    reader.GetDecimal("ticket_preis"),

                MaximaleTickets =
                    reader.IsDBNull(
                        reader.GetOrdinal("maximale_tickets"))
                    ? 0
                    : reader.GetInt32("maximale_tickets"),

                VerkaufteTickets =
                    reader.IsDBNull(
                        reader.GetOrdinal("verkaufte_tickets"))
                    ? 0
                    : reader.GetInt32("verkaufte_tickets"),

                BildUrl =
                    reader.IsDBNull(
                        reader.GetOrdinal("bild_url"))
                    ? null
                    : reader.GetString("bild_url")
            };
        }
    }
}
