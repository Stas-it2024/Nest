using MySql.Data.MySqlClient;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Data;

namespace Nest.Data
{
    // Administrative input deliberately has no sales counter or creation metadata.
    public sealed record EventUpdateRequest(int Id, string Title, string? Description,
        DateTime EventDate, TimeSpan EventTime, string? Location, decimal TicketPrice,
        int MaxTickets, string? ImageUrl, string Status);

    public enum EventUpdateStatus
    {
        Success, NotFound, InvalidCapacity, CapacityBelowSold, InvalidStatus,
        InvalidInventory, CommittedWithCleanupFailure
    }

    public sealed record EventUpdateResult(EventUpdateStatus Status, Event? Current = null);

    public sealed class EventCommitUncertainException : Exception
    {
        public string? TicketCode { get; }
        public EventCommitUncertainException(Exception inner, string? ticketCode)
            : base("Das Ergebnis der Transaktion konnte nicht bestätigt werden.", inner)
            => TicketCode = ticketCode;
    }

    public sealed class EventRollbackUncertainException : Exception
    {
        public EventRollbackUncertainException(Exception? original, Exception rollback)
            : base("Der Rollback konnte nicht bestätigt werden.", original ?? rollback)
            => Data["RollbackException"] = rollback;
    }

    public enum TicketBookingStatus
    {
        Success,
        EventNotFound,
        EventInPast,
        InvalidQuantity,
        InsufficientTickets,
        MissingGuestName,
        MissingGuestEmail,
        MissingGuestPhone,
        EventNotActive,
        InvalidInventory,
        CommittedWithCleanupFailure
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
            WHERE veranstaltungs_datum >= CURDATE() AND status = 'Aktiv'
            ORDER BY veranstaltungs_datum, veranstaltungs_zeit;";

        public List<Event> GetAll()
        {
            var veranstaltungen = new List<Event>();

            using var conn = DB.GetConnection();
            DB.OpenConnection(conn);

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
                    MaxTickets = reader.IsDBNull(reader.GetOrdinal("maximale_tickets"))
                        ? 0 : reader.GetInt32("maximale_tickets"),
                    SoldTickets = reader.IsDBNull(reader.GetOrdinal("verkaufte_tickets"))
                        ? 0 : reader.GetInt32("verkaufte_tickets"),

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
            DB.OpenConnection(conn);

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
            cmd.Parameters.AddWithValue("@verkaufte_tickets", 0);
            cmd.Parameters.AddWithValue("@bild_url", eventItem.ImageUrl);
            cmd.Parameters.AddWithValue("@status", eventItem.Status);
            cmd.Parameters.AddWithValue("@erstellt_von", eventItem.CreatedBy);

            cmd.ExecuteNonQuery();

            eventItem.Id = (int)cmd.LastInsertedId;
        }

        public EventUpdateResult Update(EventUpdateRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ExecuteEventTransaction((conn, transaction) =>
            {
                var current = LockEvent(conn, transaction, request.Id);
                if (current == null) return new EventUpdateResult(EventUpdateStatus.NotFound);
                if (InvalidInventory(current)) return new EventUpdateResult(EventUpdateStatus.InvalidInventory, current);
                if (request.Status is not ("Aktiv" or "Inaktiv" or "Ausgebucht"))
                    return new EventUpdateResult(EventUpdateStatus.InvalidStatus, current);
                if (request.MaxTickets <= 0) return new EventUpdateResult(EventUpdateStatus.InvalidCapacity, current);
                if (request.MaxTickets < current.SoldTickets)
                    return new EventUpdateResult(EventUpdateStatus.CapacityBelowSold, current);

                const string sql = @"
                    UPDATE veranstaltungen
                    SET titel = @Title, beschreibung = @Description,
                        veranstaltungs_datum = @EventDate, veranstaltungs_zeit = @EventTime,
                        ort = @Location, ticket_preis = @TicketPrice,
                        maximale_tickets = @MaxTickets, bild_url = @ImageUrl, status = @Status
                    WHERE id = @EventId;";
                WithResource(new MySqlCommand(sql, conn, transaction), cmd =>
                {
                    cmd.Parameters.AddWithValue("@EventId", request.Id);
                    cmd.Parameters.AddWithValue("@Title", request.Title);
                    cmd.Parameters.AddWithValue("@Description", (object?)request.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@EventDate", request.EventDate.Date);
                    cmd.Parameters.AddWithValue("@EventTime", request.EventTime);
                    cmd.Parameters.AddWithValue("@Location", (object?)request.Location ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@TicketPrice", request.TicketPrice);
                    cmd.Parameters.AddWithValue("@MaxTickets", request.MaxTickets);
                    cmd.Parameters.AddWithValue("@ImageUrl", (object?)request.ImageUrl ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", request.Status);
                    // UseAffectedRows=false: a matched unchanged row also returns 1.
                    if (cmd.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Die Veranstaltung konnte nicht eindeutig aktualisiert werden.");
                    return true;
                });
                return new EventUpdateResult(EventUpdateStatus.Success,
                    LockEvent(conn, transaction, request.Id)
                    ?? throw new InvalidOperationException("Die aktualisierte Veranstaltung fehlt."));
            }, r => r.Status == EventUpdateStatus.Success,
                r => r with { Status = EventUpdateStatus.CommittedWithCleanupFailure });
        }

        public void Delete(int eventId)
        {
            using var conn = DB.GetConnection();
            DB.OpenConnection(conn);

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
            DB.OpenConnection(connection);

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

            var ticketCode = GenerateTicketCode();
            return ExecuteEventTransaction((connection, transaction) =>
            {
                var current = LockEvent(connection, transaction, request.EventId);
                if (current == null) return Failed(TicketBookingStatus.EventNotFound);
                if (current.Status != "Aktiv") return Failed(TicketBookingStatus.EventNotActive);
                if (InvalidInventory(current)) return Failed(TicketBookingStatus.InvalidInventory);
                if (current.EventDate.Date + current.EventTime < DateTime.Now)
                    return Failed(TicketBookingStatus.EventInPast);
                var maximumTickets = current.MaxTickets;
                var soldTickets = current.SoldTickets;
                var unitPrice = current.TicketPrice;

                // Die Verfügbarkeit wird unter der Sperre erneut geprüft und ist damit aktueller als die Anzeige.
                if ((long)soldTickets + request.Quantity
                    > maximumTickets)
                {
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

                WithResource(new MySqlCommand(
                        insertOrderSql,
                        connection,
                        transaction), insertCommand =>
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
                    return true;
                });

                // Danach wird der verkaufte Bestand erhöht; beide Änderungen gehören fachlich zusammen.
                const string updateEventSql = @"
                    UPDATE veranstaltungen
                    SET verkaufte_tickets =
                        COALESCE(verkaufte_tickets, 0) + @Quantity
                    WHERE id = @EventId;";

                WithResource(new MySqlCommand(
                        updateEventSql,
                        connection,
                        transaction), updateCommand =>
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
                    return true;
                });

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
            }, r => r.Status == TicketBookingStatus.Success,
                r => new TicketBookingResult
                {
                    Status = TicketBookingStatus.CommittedWithCleanupFailure,
                    TicketCode = r.TicketCode, UnitPrice = r.UnitPrice,
                    TotalPrice = r.TotalPrice, RemainingTickets = r.RemainingTickets
                }, ticketCode);
        }

        private static bool InvalidInventory(Event current) => current.SoldTickets < 0
            || current.MaxTickets <= 0 || current.SoldTickets > current.MaxTickets;

        private static Event? LockEvent(MySqlConnection conn, MySqlTransaction transaction, int id)
        {
            const string sql = @"
                SELECT id, titel, beschreibung, veranstaltungs_datum, veranstaltungs_zeit,
                       ort, ticket_preis, maximale_tickets, verkaufte_tickets, bild_url,
                       status, erstellt_von, erstellt_am
                FROM veranstaltungen WHERE id = @EventId FOR UPDATE;";
            return WithResource(new MySqlCommand(sql, conn, transaction), cmd =>
            {
                cmd.Parameters.Add("@EventId", MySqlDbType.Int32).Value = id;
                return WithResource(cmd.ExecuteReader(), reader =>
                {
                    if (!reader.Read()) return null;
                    string? Text(string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? null : reader.GetString(name);
                    int Count(string name) => reader.IsDBNull(reader.GetOrdinal(name)) ? 0 : reader.GetInt32(name);
                    return new Event
                    {
                        Id = reader.GetInt32("id"), Title = reader.GetString("titel"),
                        Description = Text("beschreibung"), Location = Text("ort"), ImageUrl = Text("bild_url"),
                        EventDate = reader.GetDateTime("veranstaltungs_datum").Date,
                        EventTime = reader.IsDBNull(reader.GetOrdinal("veranstaltungs_zeit")) ? TimeSpan.Zero
                            : reader.GetTimeSpan(reader.GetOrdinal("veranstaltungs_zeit")),
                        TicketPrice = reader.GetDecimal("ticket_preis"), MaxTickets = Count("maximale_tickets"),
                        SoldTickets = Count("verkaufte_tickets"), Status = reader.GetString("status"),
                        CreatedBy = reader.IsDBNull(reader.GetOrdinal("erstellt_von")) ? null : reader.GetInt32("erstellt_von"),
                        DateCreated = reader.IsDBNull(reader.GetOrdinal("erstellt_am")) ? default : reader.GetDateTime("erstellt_am")
                    };
                });
            });
        }

        // Local to this Repository: preserve SQL/read failures if command/reader cleanup also fails.
        private static T WithResource<R, T>(R resource, Func<R, T> action) where R : IDisposable
        {
            Exception? original = null;
            try { return action(resource); }
            catch (Exception ex) { original = ex; throw; }
            finally
            {
                try { resource.Dispose(); }
                catch (Exception ex) when (original != null) { original.Data["ResourceDisposeException"] = ex; }
            }
        }

        private static T ExecuteEventTransaction<T>(Func<MySqlConnection, MySqlTransaction, T> action,
            Func<T, bool> isSuccess, Func<T, T> cleanupFailure, string? ticketCode = null)
        {
            MySqlConnection? conn = null;
            MySqlTransaction? transaction = null;
            Exception? original = null;
            Exception? cleanup = null;
            bool committed = false;
            T result;
            try
            {
                conn = DB.GetConnection();
                DB.OpenConnection(conn);
                transaction = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                result = action(conn, transaction);
                if (isSuccess(result))
                {
                    try { transaction.Commit(); }
                    catch (Exception ex) { throw new EventCommitUncertainException(ex, ticketCode); }
                    committed = true;
                }
            }
            catch (Exception ex) { original = ex; throw; }
            finally
            {
                Exception? rollback = null;
                if (transaction != null && !committed)
                {
                    try
                    {
                        transaction.Rollback();
                        if (original != null) original.Data["RollbackConfirmed"] = true;
                    }
                    catch (Exception ex) { rollback = ex; if (original != null) original.Data["RollbackException"] = ex; }
                }
                foreach (var resource in new IDisposable?[] { transaction, conn })
                {
                    try { resource?.Dispose(); }
                    catch (Exception ex)
                    {
                        cleanup ??= ex;
                        if (original != null) original.Data[resource == transaction ? "TransactionDisposeException" : "ConnectionDisposeException"] = ex;
                    }
                }
                // An attempted Commit remains uncertain even if a subsequent Rollback succeeds.
                if (rollback != null && original is not EventCommitUncertainException)
                    throw new EventRollbackUncertainException(original, rollback);
                if (!committed && original == null && cleanup != null)
                    throw new InvalidOperationException("Die Transaktion konnte nicht sicher beendet werden.", cleanup);
            }
            return committed && cleanup != null ? cleanupFailure(result) : result;
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
