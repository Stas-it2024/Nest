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

            using var conn = DB.GetConnection();
            conn.Open();
            // Lesen, Prüfen und Aktualisieren bilden eine Einheit und dürfen nicht teilweise ausgeführt werden.
            using var transaction = conn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);

            try
            {
                // FOR UPDATE sperrt diese Reservierung bis Commit oder Rollback gegen parallele Zahlungen.
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
                    LIMIT 1
                    FOR UPDATE;";

                using var selectCommand = new MySqlCommand(selectSql, conn, transaction);
                selectCommand.Parameters.Add("@ReservationCode", MySqlDbType.VarChar).Value = reservationCode.Trim();

                int reservationId;
                ReservationOverview reservation;
                using (var reader = selectCommand.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        transaction.Rollback();
                        return PaymentUpdateResult.NotFound;
                    }

                    reservationId = reader.GetInt32("id");
                    reservation = ReadPaymentOverview(reader);
                }

                // Der offene Betrag wird unter der Sperre erneut geprüft, weil er sich seit der Anzeige geändert haben könnte.
                var outstandingAmount = reservation.OutstandingAmount;
                if (outstandingAmount <= 0m)
                {
                    transaction.Rollback();
                    updatedReservation = reservation;
                    return PaymentUpdateResult.FullyPaid;
                }

                if (paymentAmount > outstandingAmount)
                {
                    transaction.Rollback();
                    updatedReservation = reservation;
                    return PaymentUpdateResult.AmountExceedsOutstanding;
                }

                const string updateSql = @"
                    UPDATE reservierungen
                    SET gezahlter_betrag = gezahlter_betrag + @PaymentAmount,
                        zahlungsart = 'Online'
                    WHERE id = @ReservationId;";
                using var updateCommand = new MySqlCommand(updateSql, conn, transaction);
                updateCommand.Parameters.Add("@PaymentAmount", MySqlDbType.Decimal).Value = paymentAmount;
                updateCommand.Parameters.Add("@ReservationId", MySqlDbType.Int32).Value = reservationId;
                updateCommand.ExecuteNonQuery();

                reservation.AmountPaid += paymentAmount;
                reservation.PaymentMethod = "Online";
                updatedReservation = reservation;
                // Erst Commit macht die erfolgreich geprüfte Zahlung dauerhaft sichtbar.
                transaction.Commit();
                return PaymentUpdateResult.Success;
            }
            catch
            {
                // Bei jedem unerwarteten Fehler stellt Rollback den vorherigen Datenbankzustand wieder her.
                transaction.Rollback();
                throw;
            }
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
                      AND rm.status = 'Verfuegbar'
                      AND rm.geloescht_am IS NULL
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM reservierungen r
                          WHERE r.zimmer_id = rm.id
                            AND r.status <> 'Storniert'
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
        {
            if (reservation.CheckOutDate.Date <= reservation.CheckInDate.Date)
            {
                return false;
            }

            using var conn = DB.GetConnection();
            conn.Open();
            // Die zweite Verfügbarkeitsprüfung liegt direkt in der Transaktion und ist deshalb für das Speichern entscheidend.
            using var transaction = conn.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);

            try
            {
                // FOR UPDATE serialisiert parallele Buchungsversuche für dasselbe Zimmer und verhindert Doppelbuchungen.
                const string lockRoomSql = @"
                    SELECT id
                    FROM zimmer
                    WHERE id = @RoomId
                      AND status = 'Verfuegbar'
                      AND geloescht_am IS NULL
                    FOR UPDATE";
                using var lockRoomCommand = new MySqlCommand(lockRoomSql, conn, transaction);
                lockRoomCommand.Parameters.AddWithValue("@RoomId", reservation.RoomId);
                if (lockRoomCommand.ExecuteScalar() == null)
                {
                    transaction.Rollback();
                    return false;
                }

                // Nach der Zimmersperre wird der Zeitraum nochmals geprüft, damit zwischen Prüfung und INSERT nichts dazwischenkommt.
                const string overlapSql = @"
                    SELECT id
                    FROM reservierungen
                    WHERE zimmer_id = @RoomId
                      AND status <> 'Storniert'
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
                    transaction.Rollback();
                    return false;
                }

                using var insertCommand = CreateInsertCommand(conn, transaction, reservation);
                reservation.Id = Convert.ToInt32(insertCommand.ExecuteScalar());
                // Nur wenn Sperre, Prüfung und INSERT erfolgreich waren, wird die Buchung fest gespeichert.
                transaction.Commit();
                return true;
            }
            catch
            {
                // Rollback nimmt alle Schritte dieser Buchung zurück; die Ausnahme wird zur Anzeige weitergegeben.
                transaction.Rollback();
                throw;
            }
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
            cmd.Parameters.AddWithValue("@PaymentMethod", (object)reservation.PaymentMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", reservation.Status);
            cmd.Parameters.AddWithValue("@ReservationCode", reservation.ReservationCode);
            cmd.Parameters.AddWithValue("@DateCreated", reservation.DateCreated);
            cmd.Parameters.AddWithValue("@SpecialRequests", (object)reservation.SpecialRequests ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ReceptionistId", (object)reservation.ReceptionistId ?? DBNull.Value);
            return cmd;
        }
    }
}
