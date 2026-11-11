using Nest.Models;
using RoomTests;
using RoomTests.Data;
using System.Globalization;

internal static class Program
{
    private static int assertions;
    private static readonly RoomRepository Repository = new();
    private static readonly RoomOriginalValues Original = new(1, "101", "Standard", 2, 50.25m,
        "Verfuegbar", "Description", "room.png", new DateTime(2026, 1, 1), null);
    private static void Check(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); assertions++; }
    private static Room Proposed(RoomOriginalValues? original = null)
    {
        var r = original ?? Original;
        return new() { Id = r.Id, RoomNumber = int.Parse(r.RoomNumber, CultureInfo.InvariantCulture),
            Type = r.Type, Capacity = r.Capacity, PricePerNight = r.PricePerNight, Status = r.Status,
            Description = r.Description, ImagePath = r.ImagePath, DateAdded = r.DateAdded ?? default, DateDelete = r.DateDelete };
    }
    private static RoomWriteResult Write(bool delete, RoomOriginalValues? expected = null) => delete
        ? Repository.Delete(expected ?? Original) : Repository.Update(Proposed(), expected ?? Original);
    private static void Main()
    {
        // Generated Repository binds exclusively to in-memory transport. Also fail closed
        // if a future test accidentally calls any non-isolated production Repository.
        Environment.SetEnvironmentVariable("NEST_INTEGRATION_TEST", "invalid-offline-test");
        Environment.SetEnvironmentVariable("NEST_INTEGRATION_CONNECTION_STRING", null);
        BaselineAndProjection();
        BusinessRules();
        FailureMatrix();
        Check(typeof(Nest.Data.RoomRepository).GetMethods().Where(m => m.Name == "Update")
            .All(m => m.GetParameters().Length == 2), "Unsafe public Update overload");
        Check(typeof(Nest.Data.RoomRepository).GetMethods().Where(m => m.Name == "Delete")
            .All(m => m.GetParameters().Single().ParameterType == typeof(RoomOriginalValues)), "Unsafe public Delete overload");
        Console.WriteLine($"PASS: {assertions} room Repository assertions; current production body, in-memory transport; no MariaDB or SQL execution.");
    }
    private static void BaselineAndProjection()
    {
        State.Reset(Original);
        Check(Repository.Update(Proposed(), Original) == RoomWriteResult.Success, "No-op found row must succeed");
        Check(State.Commits == 1 && State.Rollbacks == 0 && State.Writes == 1, "No-op lifecycle");
        Check(State.Row == Original, "No-op data");
        State.Reset(Original);
        var noImageChange = Proposed(); noImageChange.ImagePath = null;
        Check(Repository.Update(noImageChange, Original) == RoomWriteResult.Success && State.Row?.ImagePath == Original.ImagePath,
            "Preserve COALESCE image semantics");
        State.Reset(Original);
        var changed = Proposed(); changed.Description = "First administrator";
        Check(Repository.Update(changed, Original) == RoomWriteResult.Success, "First update");
        Check(Repository.Update(Proposed(), Original) == RoomWriteResult.ConcurrencyConflict, "Stale second update");
        Check(Repository.Delete(Original) == RoomWriteResult.ConcurrencyConflict, "Stale delete");
        Check(State.Writes == 1 && State.Commits == 1 && State.Row?.Description == changed.Description, "No lost update or stale delete");
        Check(Repository.Delete(State.Row!) == RoomWriteResult.Success, "Delete accepted fresh baseline");
        Check(State.Row?.DateDelete != null, "Soft delete only");

        var variations = new[] {
            Original with { RoomNumber = "0101" }, Original with { RoomNumber = "101 " },
            Original with { Type = "Suite" }, Original with { Capacity = 3 },
            Original with { PricePerNight = 50.26m }, Original with { Status = "Wartung" },
            Original with { Description = null }, Original with { Description = "" },
            Original with { Description = "description" }, Original with { Description = "Description " },
            Original with { ImagePath = null }, Original with { ImagePath = "" },
            Original with { ImagePath = "Room.png" }, Original with { ImagePath = "room.png " },
            Original with { DateAdded = null }, Original with { DateAdded = Original.DateAdded!.Value.AddTicks(1) },
            Original with { DateAdded = Original.DateAdded!.Value.AddSeconds(1) }
        };
        foreach (var delete in new[] { false, true })
        foreach (var current in variations)
        {
            State.Reset(current);
            Check(Write(delete) == RoomWriteResult.ConcurrencyConflict, "Each changed field must conflict");
            Check(State.Writes == 0 && State.Commits == 0 && State.Rollbacks == 1, "Conflict before writes/commit");
            Check(State.Row == current && State.Commands.Count == 1, "Conflict cannot lock/change reservations");
            Check(State.Commands[0].Sql.Contains("FOR UPDATE") && State.Commands[0].Sql.Contains("hinzugefuegt_am"), "Full locking projection");
        }
        foreach (var delete in new[] { false, true })
        {
            var nulls = Original with { Description = null, ImagePath = null, DateAdded = null };
            State.Reset(nulls with { Description = "" });
            Check(Write(delete, nulls) == RoomWriteResult.ConcurrencyConflict, "NULL description differs from empty");
            State.Reset(nulls with { ImagePath = "" });
            Check(Write(delete, nulls) == RoomWriteResult.ConcurrencyConflict, "NULL path differs from empty");
            State.Reset(nulls with { DateAdded = default(DateTime) });
            Check(Write(delete, nulls) == RoomWriteResult.ConcurrencyConflict, "NULL date differs from default presentation date");
        }
        foreach (var delete in new[] { false, true })
        foreach (var absent in new RoomOriginalValues?[] { null, Original with { DateDelete = DateTime.Today } })
        {
            State.Reset(absent);
            Check(Write(delete) == RoomWriteResult.Missing, "Absent/soft-deleted row");
            Check(State.Writes == 0 && State.Commits == 0 && State.Rollbacks == 1, "Missing lifecycle");
        }
        State.Reset(Original);
        var wrongId = Proposed(); wrongId.Id = 2;
        Check(Repository.Update(wrongId, Original) == RoomWriteResult.ConcurrencyConflict, "Mismatching IDs");
        Check(State.Events.Count == 0, "ID validation precedes connection");
        foreach (var raw in new[] { Original with { RoomNumber = "0101", DateAdded = null },
            Original with { Description = null, ImagePath = "" }, Original with { PricePerNight = 50.250m } })
        {
            State.Reset(raw);
            var read = Repository.GetAll().Single();
            Check(read.OriginalValues == raw, "Lossless Repository read projection");
            Check(read.DateAdded == (raw.DateAdded ?? default), "Nullable added date presentation");
            Check(Repository.Update(Proposed(raw), read.OriginalValues!) == RoomWriteResult.Success, "Raw baseline accepted");
        }
        State.Reset(Original with { DateAdded = null });
        Check(Repository.Delete(State.Row!) == RoomWriteResult.Success, "Nullable-date delete baseline");
        Check(State.Row != null && State.Row.DateAdded == null && State.Row.Status == Original.Status, "Delete retains date/status");
    }
    private static void BusinessRules()
    {
        foreach (var delete in new[] { false, true })
        {
            State.Reset(Original); State.Occupied = true;
            Check(Write(delete) == RoomWriteResult.Occupied, "Occupancy protection");
            Check(State.Writes == 0 && State.Rollbacks == 1, "Occupancy refusal");
            Check(State.Commands[0].Sql.Contains("FROM zimmer") && State.Commands[0].Sql.Contains("FOR UPDATE") &&
                State.Commands[1].Sql.Contains("FROM reservierungen") && State.Commands[1].Sql.Contains("FOR UPDATE"), "Room-first ordering");
        }
        State.Reset(Original); State.Occupied = true;
        var occupied = Proposed(); occupied.Status = "Belegt";
        Check(Repository.Update(occupied, Original) == RoomWriteResult.Success, "Occupied room remains Belegt");
        State.Reset(Original);
        Check(Repository.Update(occupied, Original) == RoomWriteResult.InvalidStatus, "Cannot invent occupancy");
        foreach (var status in new[] { "Verfuegbar", "Reserviert", "Wartung" })
        {
            State.Reset(Original); var r = Proposed(); r.Status = status;
            Check(Repository.Update(r, Original) == RoomWriteResult.Success, "Existing allowed status");
        }
        State.Reset(Original); State.Future = true;
        Check(Repository.Delete(Original) == RoomWriteResult.FutureReservation, "Inclusive future protection");
        Check(State.Commands[1].Sql.Contains("abreise_datum >= @Today") &&
            Equals(State.Commands[1].Parameters["@Today"], DateTime.Today), "Client Today parameter");
        State.Reset(Original); State.Future = true;
        Check(Repository.Update(Proposed(), Original) == RoomWriteResult.Success, "Future bookings do not change Update rules");
        State.Reset(Original);
        Check(Repository.Delete(Original) == RoomWriteResult.Success, "Unprotected delete");
        Check(State.Row!.Status == Original.Status && State.Commands.Last().Sql.Contains("SET geloescht_am") &&
            !State.Commands.Last().Sql.Contains("DELETE FROM"), "No status change or physical delete");
        var addOccupied = Proposed(); addOccupied.Status = "Belegt"; State.Reset(Original);
        Check(Repository.Add(addOccupied) == RoomWriteResult.InvalidStatus && State.Events.Count == 0, "Add occupancy rule");
        foreach (var status in new[] { "Verfuegbar", "Reserviert", "Wartung" })
        foreach (var fault in new[] { "", "commit", "connection-dispose", "insert", "insert,transaction-dispose" })
        {
            State.Reset(null); State.Fault = fault;
            var room = Proposed(); room.Id = 0; room.Status = status;
            var result = Repository.Add(room);
            var expected = fault == "commit" ? RoomWriteResult.CommitUncertain : fault.Contains("insert") ? RoomWriteResult.TechnicalFailure :
                fault == "connection-dispose" ? RoomWriteResult.CommittedWithCleanupFailure : RoomWriteResult.Success;
            Check(result == expected, "Add transaction lifecycle");
            Check(room.Id == (result is RoomWriteResult.Success or RoomWriteResult.CommittedWithCleanupFailure ? 9 : 0), "Add ID only on confirmed commit");
        }
    }
    private static void FailureMatrix()
    {
        foreach (var delete in new[] { false, true })
        {
            foreach (var count in new[] { 0, 1, 2 })
            {
                State.Reset(Original); State.Affected = count;
                Check(Write(delete) == (count == 1 ? RoomWriteResult.Success : RoomWriteResult.TechnicalFailure), "Affected row contract");
                Check(State.Writes == 1 && State.Commits == (count == 1 ? 1 : 0) && State.Rollbacks == (count == 1 ? 0 : 1), "Affected row transaction");
            }
            foreach (var fault in new[] { "create", "open", "begin", "read", "reader-read", "reader-dispose", "command-dispose", "update", "1062", "commit",
                "update,rollback", "commit,rollback", "update,transaction-dispose,connection-dispose", "1062,transaction-dispose,connection-dispose",
                "commit,transaction-dispose,connection-dispose", "1062,write-command-dispose", "update,write-command-dispose",
                "transaction-dispose", "connection-dispose", "transaction-dispose,connection-dispose" })
            {
                State.Reset(Original); State.Fault = fault;
                var result = Write(delete);
                var expected = fault.Contains("commit") ? RoomWriteResult.CommitUncertain : fault.Contains("rollback") ? RoomWriteResult.UncertainOutcome :
                    fault.StartsWith("1062") ? RoomWriteResult.NumberConflict : fault is "transaction-dispose" or "connection-dispose" or "transaction-dispose,connection-dispose"
                        ? RoomWriteResult.CommittedWithCleanupFailure : RoomWriteResult.TechnicalFailure;
                Check(result == expected, "Failure classification: " + fault);
                Check(State.Writes <= 1 && State.Commits <= 1, "No write retries");
                if (result == RoomWriteResult.CommittedWithCleanupFailure)
                    Check(State.Commits == 1 && State.Rollbacks == 0, "No rollback after confirmed commit");
                if (!fault.StartsWith("create")) Check(State.ConnectionsDisposed == 1, "Connection always disposed");
                if (!new[] { "create", "open", "begin" }.Contains(fault)) Check(State.TransactionsDisposed == 1, "Transaction always disposed");
            }
            State.Reset(Original with { Description = "Other" }); State.Fault = "rollback";
            Check(Write(delete) == RoomWriteResult.UncertainOutcome && State.Writes == 0, "Rollback failure on conflict stays uncertain");
            State.Reset(Original with { Description = "Other" }); State.Fault = "transaction-dispose,connection-dispose";
            Check(Write(delete) == RoomWriteResult.ConcurrencyConflict && State.Rollbacks == 1, "Cleanup cannot mask confirmed refusal");
        }
    }
}
