using MySql.Data.MySqlClient;
using Nest.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nest.Data
{
    public enum PaymentUpdateResult
    {
        Success,
        InvalidAmount,
        NotFound,
        FullyPaid,
        AmountExceedsOutstanding
    }

    public sealed class ReservationCommitUncertainException : Exception
    {
        public ReservationCommitUncertainException(Exception innerException)
            : base("Das Ergebnis der Transaktion konnte nicht bestätigt werden.", innerException)
        {
        }
    }

    public class ReservationRepository
    {
        private const string InsertSql = @"
            INSERT INTO reservierungen
                (gast_id, zimmer_id, anreise_datum, abreise_datum, naechte, erwachsene, kinder,
                 gesamtbetrag, gezahlter_betrag, zahlungsart, status, reservierungs_code,
                 erstellt_am, sonderwuensche, rezeptionist_id)
            VALUES
                (@GuestId, @RoomId, @CheckInDate, @CheckOutDate, @Nights, @Adults, @Children,
                 @TotalAmount, @AmountPaid, @PaymentMethod, @Status, @ReservationCode,
                 @DateCreated, @SpecialRequests, @ReceptionistId);
            SELECT LAST_INSERT_ID();";

        public List<ReservationOverview> GetAll()
        {
            var list = new List<ReservationOverview>();

            using var conn = DB.GetConnection();
            conn.Open();

            const string sql = @"
                SELECT
                    r.reservierungs_code,
                    CONCAT(g.vorname, ' ', g.nachname) AS guest,
                    rm.zimmer_nummer,
                    rm.zimmer_typ,
                    r.anreise_datum,
                    r.abreise_datum,
                    r.naechte,
                    r.erwachsene,
                    r.kinder,
                    r.gesamtbetrag,
                    r.gezahlter_betrag,
                    r.zahlungsart,
                    r.status
                FROM reservierungen r
                JOIN gaeste g ON r.gast_id = g.id
                JOIN zimmer rm ON r.zimmer_id = rm.id
                ORDER BY r.erstellt_am DESC;";

            using var cmd = new MySqlCommand(sql, conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(new ReservationOverview
                {
                    ReservationCode = reader.GetString("reservierungs_code"),
                    Guest = reader.GetString("guest"),
                    Room = reader.GetString("zimmer_nummer"),
                    Type = reader.GetString("zimmer_typ"),
                    CheckInDate = reader.GetDateTime("anreise_datum"),
                    CheckOutDate = reader.GetDateTime("abreise_datum"),
                    Nights = reader.GetInt32("naechte"),
                    Adults = reader.GetInt32("erwachsene"),
                    Children = reader.GetInt32("kinder"),
                    TotalAmount = reader.GetDecimal("gesamtbetrag"),
                    AmountPaid = reader.GetDecimal("gezahlter_betrag"),
                    PaymentMethod = reader.GetString("zahlungsart"),
                    Status = reader.GetString("status")
                });
            }

            return list;
        }
        public List<ReservationOverview> GetByGuestId(int guestId)
        {
            var list = new List<ReservationOverview>();

            using var conn = DB.GetConnection();
            conn.Open();

            const string sql = @"
                        SELECT
                            r.reservierungs_code,
                            CONCAT(g.vorname, ' ', g.nachname) AS guest,
                            rm.zimmer_nummer,
                            rm.zimmer_typ,
                            r.anreise_datum,
                            r.abreise_datum,
                            DATEDIFF(r.abreise_datum, r.anreise_datum) AS Nights,
                            r.erwachsene,
                            r.kinder,
                            r.gesamtbetrag,
                            r.gezahlter_betrag,
                            r.zahlungsart,
                            r.status
                        FROM reservierungen r
                        JOIN gaeste g ON r.gast_id = g.id
                        JOIN zimmer rm ON r.zimmer_id = rm.id
                        WHERE r.gast_id = @guestId
                        ORDER BY r.anreise_datum DESC;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@guestId", guestId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ReservationOverview
                {
                    ReservationCode = reader.GetString("reservierungs_code"),
                    Guest = reader.GetString("guest"),
                    Room = reader.GetString("zimmer_nummer"),
                    Type = reader.GetString("zimmer_typ"),
                    CheckInDate = reader.GetDateTime("anreise_datum"),
                    CheckOutDate = reader.GetDateTime("abreise_datum"),
                    Nights = reader.GetInt32("Nights"),
                    Adults = reader.GetInt32("erwachsene"),
                    Children = reader.GetInt32("kinder"),
                    TotalAmount = reader.GetDecimal("gesamtbetrag"),
                    Status = reader.GetString("status"),
                    AmountPaid = reader.GetDecimal("gezahlter_betrag"),
                    PaymentMethod = reader.IsDBNull(reader.GetOrdinal("zahlungsart"))
                        ? string.Empty
                        : reader.GetString("zahlungsart")
                });
            }

            return list;

        }

        public ReservationOverview? GetPaymentDetailsByCode(string reservationCode)
        {
            // Das Fragezeichen erlaubt null als klares Ergebnis, wenn kein passender Datensatz existiert.
            if (string.IsNullOrWhiteSpace(reservationCode))
            {
                return null;
            }

            using var conn = DB.GetConnection();
            conn.Open();

            const string sql = @"
                SELECT
                    r.reservierungs_code,
                    CONCAT(g.vorname, ' ', g.nachname) AS guest,
                    rm.zimmer_nummer,
                    rm.zimmer_typ,
                    r.anreise_datum,
                    r.abreise_datum,
                    r.naechte,
                    r.erwachsene,
                    r.kinder,
                    r.gesamtbetrag,
                    r.gezahlter_betrag,
                    r.zahlungsart,
                    r.status
                FROM reservierungen r
                JOIN gaeste g ON r.gast_id = g.id
                JOIN zimmer rm ON r.zimmer_id = rm.id
                WHERE r.reservierungs_code = @ReservationCode
                LIMIT 1;";

            // Der Code wird als Parameter übergeben und nicht in den SQL-Text eingesetzt.
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.Add("@ReservationCode", MySqlDbType.VarChar).Value = reservationCode.Trim();
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadPaymentOverview(reader) : null;
        }

        public PaymentUpdateResult TryApplyOnlinePayment(
            string reservationCode,
            decimal paymentAmount,
            out ReservationOverview? updatedReservation)
        {
            updatedReservation = null;
            if (string.IsNullOrWhiteSpace(reservationCode))
            {
                return PaymentUpdateResult.NotFound;
            }

            if (paymentAmount <= 0m)
            {
                return PaymentUpdateResult.InvalidAmount;
            }

            ReservationOverview? paymentSnapshot = null;
            var paymentResult = PaymentUpdateResult.NotFound;
            ExecuteReservationTransaction((conn, transaction) =>
            {
                // FOR UPDATE sperrt diese Reservierung bis Commit oder Rollback gegen parallele Zahlungen.
                const string lockSql = @"
                    SELECT id FROM reservierungen
                    WHERE reservierungs_code = @ReservationCode
                    LIMIT 1 FOR UPDATE;";
                using var lockCommand = new MySqlCommand(lockSql, conn, transaction);
                lockCommand.Parameters.Add("@ReservationCode", MySqlDbType.VarChar).Value = reservationCode.Trim();
                var lockedId = lockCommand.ExecuteScalar();
                if (lockedId == null || lockedId == DBNull.Value)
                {
                    return false;
                }

                // Die Projektion liest nach der Sperre den aktuellen Betrag, ohne Zimmer oder Gast zu sperren.
                const string selectSql = @"
                    SELECT
                        r.id,
                        r.reservierungs_code,
                        CONCAT(g.vorname, ' ', g.nachname) AS guest,
                        rm.zimmer_nummer,
                        rm.zimmer_typ,
                        r.anreise_datum,
                        r.abreise_datum,
                        r.naechte,
                        r.erwachsene,
                        r.kinder,
                        r.gesamtbetrag,
                        r.gezahlter_betrag,
                        r.zahlungsart,
                        r.status
                    FROM reservierungen r
                    JOIN gaeste g ON r.gast_id = g.id
                    JOIN zimmer rm ON r.zimmer_id = rm.id
                    WHERE r.reservierungs_code = @ReservationCode
                    LIMIT 1;";

                using var selectCommand = new MySqlCommand(selectSql, conn, transaction);
                selectCommand.Parameters.Add("@ReservationCode", MySqlDbType.VarChar).Value = reservationCode.Trim();

                int reservationId;
                ReservationOverview reservation;
                using (var reader = selectCommand.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return false;
                    }

                    reservationId = reader.GetInt32("id");
                    reservation = ReadPaymentOverview(reader);
                }

                // Der offene Betrag wird unter der Sperre erneut geprüft, weil er sich seit der Anzeige geändert haben könnte.
                var outstandingAmount = reservation.OutstandingAmount;
                if (outstandingAmount <= 0m)
                {
                    paymentSnapshot = reservation;
                    paymentResult = PaymentUpdateResult.FullyPaid;
                    return false;
                }

                if (paymentAmount > outstandingAmount)
                {
                    paymentSnapshot = reservation;
                    paymentResult = PaymentUpdateResult.AmountExceedsOutstanding;
                    return false;
                }

                const string updateSql = @"
                    UPDATE reservierungen
                    SET gezahlter_betrag = gezahlter_betrag + @PaymentAmount,
                        zahlungsart = 'Online'
                    WHERE id = @ReservationId;";
                using var updateCommand = new MySqlCommand(updateSql, conn, transaction);
                updateCommand.Parameters.Add("@PaymentAmount", MySqlDbType.Decimal).Value = paymentAmount;
                updateCommand.Parameters.Add("@ReservationId", MySqlDbType.Int32).Value = reservationId;
                if (updateCommand.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Die Zahlung konnte nicht eindeutig aktualisiert werden.");

                reservation.AmountPaid += paymentAmount;
                reservation.PaymentMethod = "Online";
                paymentSnapshot = reservation;
                paymentResult = PaymentUpdateResult.Success;
                return true;
            });
            updatedReservation = paymentSnapshot;
            return paymentResult;
        }

        private static ReservationOverview ReadPaymentOverview(MySqlDataReader reader)
        {
            return new ReservationOverview
            {
                ReservationCode = reader.GetString("reservierungs_code"),
                Guest = reader.GetString("guest"),
                Room = reader.GetString("zimmer_nummer"),
                Type = reader.GetString("zimmer_typ"),
                CheckInDate = reader.GetDateTime("anreise_datum"),
                CheckOutDate = reader.GetDateTime("abreise_datum"),
                Nights = reader.GetInt32("naechte"),
                Adults = reader.GetInt32("erwachsene"),
                Children = reader.GetInt32("kinder"),
                TotalAmount = reader.GetDecimal("gesamtbetrag"),
                AmountPaid = reader.GetDecimal("gezahlter_betrag"),
                PaymentMethod = reader.IsDBNull(reader.GetOrdinal("zahlungsart"))
                    ? string.Empty
                    : reader.GetString("zahlungsart"),
                Status = reader.GetString("status")
            };
        }

        public void Add(Reservation reservation)
        {
            if (!TryAddIfRoomAvailable(reservation))
            {
                throw new InvalidOperationException(
                    "Das Zimmer ist im ausgewählten Zeitraum bereits reserviert.");
            }
        }

        public bool IsRoomAvailable(int roomId, DateTime requestedCheckIn, DateTime requestedCheckOut)
        {
            // Diese erste Prüfung liefert schnelles Feedback für die Oberfläche, bevor gespeichert wird.
            if (requestedCheckOut.Date <= requestedCheckIn.Date)
            {
                return false;
            }

            using var conn = DB.GetConnection();
            conn.Open();
            const string sql = @"
                SELECT EXISTS
                (
                    SELECT 1
                    FROM zimmer rm
                    WHERE rm.id = @RoomId
                      AND rm.status IN ('Verfuegbar', 'Reserviert', 'Belegt')
                      AND rm.geloescht_am IS NULL
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM reservierungen r
                          WHERE r.zimmer_id = rm.id
                            AND r.status IN ('Reserviert', 'Eingecheckt')
                            AND @RequestedCheckIn < r.abreise_datum
                            AND @RequestedCheckOut > r.anreise_datum
                      )
                )";
            // Die bekannte Überschneidungsformel erkennt auch teilweise überlappende Buchungszeiträume.
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@RoomId", roomId);
            cmd.Parameters.Add("@RequestedCheckIn", MySqlDbType.Date).Value = requestedCheckIn.Date;
            cmd.Parameters.Add("@RequestedCheckOut", MySqlDbType.Date).Value = requestedCheckOut.Date;
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }

        public bool TryAddIfRoomAvailable(Reservation reservation)
            => TryAddIfRoomAvailable(reservation, null);

        public bool TryAddIfRoomAvailable(Reservation reservation, Guest? guest)
        {
            var validationError = GetBookingValidationError(reservation);
            if (validationError != null)
                throw new ArgumentException(validationError, nameof(reservation));
            if (guest != null && (string.IsNullOrWhiteSpace(guest.FirstName) || string.IsNullOrWhiteSpace(guest.LastName)))
                throw new ArgumentException("Bitte Vorname und Nachname eingeben.", nameof(guest));

            int reservationId = 0, guestId = reservation.GuestId;
            var saved = ExecuteReservationTransaction((conn, transaction) =>
            {
                // FOR UPDATE serialisiert parallele Buchungsversuche für dasselbe Zimmer und verhindert Doppelbuchungen.
                const string lockRoomSql = @"
                    SELECT kapazitaet
                    FROM zimmer
                    WHERE id = @RoomId
                      AND status IN ('Verfuegbar', 'Reserviert', 'Belegt')
                      AND geloescht_am IS NULL
                    FOR UPDATE";
                using var lockRoomCommand = new MySqlCommand(lockRoomSql, conn, transaction);
                lockRoomCommand.Parameters.AddWithValue("@RoomId", reservation.RoomId);
                var capacity = lockRoomCommand.ExecuteScalar();
                if (capacity == null || capacity == DBNull.Value)
                {
                    return false;
                }
                if ((long)reservation.Adults + reservation.Children > Convert.ToInt32(capacity))
                    throw new ArgumentException("Die Zimmerkapazität darf nicht überschritten werden.", nameof(reservation));

                // Nach der Zimmersperre wird der Zeitraum nochmals geprüft, damit zwischen Prüfung und INSERT nichts dazwischenkommt.
                const string overlapSql = @"
                    SELECT id
                    FROM reservierungen
                    WHERE zimmer_id = @RoomId
                      AND status IN ('Reserviert', 'Eingecheckt')
                      AND @RequestedCheckIn < abreise_datum
                      AND @RequestedCheckOut > anreise_datum
                    LIMIT 1
                    FOR UPDATE";
                using var overlapCommand = new MySqlCommand(overlapSql, conn, transaction);
                overlapCommand.Parameters.AddWithValue("@RoomId", reservation.RoomId);
                overlapCommand.Parameters.Add("@RequestedCheckIn", MySqlDbType.Date).Value = reservation.CheckInDate.Date;
                overlapCommand.Parameters.Add("@RequestedCheckOut", MySqlDbType.Date).Value = reservation.CheckOutDate.Date;
                if (overlapCommand.ExecuteScalar() != null)
                {
                    return false;
                }

                guestId = guest == null
                    ? reservation.GuestId
                    : new GastRepository().FindOrAdd(guest, conn, transaction);
                using var insertCommand = CreateInsertCommand(conn, transaction, reservation);
                insertCommand.Parameters["@GuestId"].Value = guestId;
                reservationId = Convert.ToInt32(insertCommand.ExecuteScalar());
                return true;
            });
            if (saved)
            {
                reservation.Id = reservationId;
                reservation.GuestId = guestId;
                if (guest != null) guest.Id = guestId;
            }
            return saved;
        }

        private static bool ExecuteReservationTransaction(Func<MySqlConnection, MySqlTransaction, bool> apply)
        {
            MySqlConnection? conn = null;
            MySqlTransaction? transaction = null;
            Exception? originalError = null;
            var committed = false;
            try
            {
                conn = DB.GetConnection();
                conn.Open();
                transaction = conn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
                if (!apply(conn, transaction))
                    return false;

                try
                {
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    // Nur ein Fehler unmittelbar in Commit erhält diesen Vertrag.
                    throw new ReservationCommitUncertainException(ex);
                }
                committed = true;
                return true;
            }
            catch (Exception ex)
            {
                originalError = ex;
                throw;
            }
            finally
            {
                Exception? cleanupError = null;
                if (transaction != null && !committed)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception rollbackError)
                    {
                        if (originalError != null)
                            originalError.Data["RollbackException"] = rollbackError;
                        else
                            cleanupError = rollbackError;
                    }
                }

                // Auch Dispose darf weder den ursprünglichen Fehler noch einen bestätigten Commit ersetzen.
                foreach (var resource in new IDisposable?[] { transaction, conn })
                {
                    try
                    {
                        resource?.Dispose();
                    }
                    catch (Exception disposeError)
                    {
                        if (originalError != null)
                            originalError.Data[resource == transaction ? "TransactionDisposeException" : "ConnectionDisposeException"] = disposeError;
                        else
                            cleanupError ??= disposeError;
                    }
                }
                if (!committed && originalError == null && cleanupError != null)
                    throw new InvalidOperationException(
                        "Der Vorgang konnte nicht sicher beendet werden. Bitte prüfen Sie den tatsächlichen Zustand.",
                        cleanupError);
            }
        }

        public static string? GetBookingValidationError(Reservation reservation)
        {
            if (!string.Equals(reservation.Status, "Reserviert", StringComparison.Ordinal))
                return "Neue Reservierungen müssen den Status 'Reserviert' haben.";
            if (reservation.CheckInDate.Date < DateTime.Today)
                return "Das Anreisedatum darf nicht in der Vergangenheit liegen.";
            if (reservation.CheckOutDate.Date <= reservation.CheckInDate.Date)
                return "Check-out muss nach Check-in liegen.";
            if (reservation.Adults < 1 || reservation.Children < 0)
                return "Bitte eine gültige Anzahl der Erwachsenen und Kinder eingeben.";
            if (reservation.TotalAmount < 0)
                return "Der Gesamtbetrag darf nicht negativ sein.";
            if (reservation.AmountPaid < 0 || reservation.AmountPaid > reservation.TotalAmount)
                return "Die Anzahlung muss zwischen 0 und dem Gesamtbetrag liegen.";
            return null;
        }

        private static MySqlCommand CreateInsertCommand(
            MySqlConnection conn,
            MySqlTransaction? transaction,
            Reservation reservation)
        {
            // Der nullable Transaktionsparameter erlaubt dieselbe parametrisierte INSERT-Definition mit oder ohne Transaktion.
            var cmd = new MySqlCommand(InsertSql, conn, transaction);
            cmd.Parameters.AddWithValue("@GuestId", reservation.GuestId);
            cmd.Parameters.AddWithValue("@RoomId", reservation.RoomId);
            cmd.Parameters.AddWithValue("@CheckInDate", reservation.CheckInDate);
            cmd.Parameters.AddWithValue("@CheckOutDate", reservation.CheckOutDate);
            cmd.Parameters.AddWithValue("@Nights", reservation.Nights);
            cmd.Parameters.AddWithValue("@Adults", reservation.Adults);
            cmd.Parameters.AddWithValue("@Children", reservation.Children);
            cmd.Parameters.AddWithValue("@TotalAmount", reservation.TotalAmount);
            cmd.Parameters.AddWithValue("@AmountPaid", reservation.AmountPaid);
            // Optionale C#-Werte werden mit DBNull.Value als echtes SQL-NULL gespeichert.
            cmd.Parameters.AddWithValue("@PaymentMethod", (object?)reservation.PaymentMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", reservation.Status);
            cmd.Parameters.AddWithValue("@ReservationCode", reservation.ReservationCode);
            cmd.Parameters.AddWithValue("@DateCreated", reservation.DateCreated);
            cmd.Parameters.AddWithValue("@SpecialRequests", (object?)reservation.SpecialRequests ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ReceptionistId", (object?)reservation.ReceptionistId ?? DBNull.Value);
            return cmd;
        }
    }
}
