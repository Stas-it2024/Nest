using Nest.Models;

namespace Nest.Synchronization;

// All fields of the existing read projections; immutable values cross the UI boundary.
public sealed record RoomSnapshot(int Id, int Number, string Type, decimal Price,
    int Capacity, string Status, string? Description, string? ImagePath,
    DateTime Added, DateTime? Deleted);
public sealed record BookingSnapshot(string Code, string Guest, string Room, string Type,
    DateTime Arrival, DateTime Departure, int Nights, int Adults, int Children,
    decimal Total, decimal Paid, string PaymentMethod, string Status);

public sealed class PollingSnapshot
{
    public IReadOnlyList<RoomSnapshot> Rooms { get; }
    public IReadOnlyList<BookingSnapshot> Bookings { get; }
    public PollingSnapshot(IEnumerable<RoomSnapshot> rooms, IEnumerable<BookingSnapshot> bookings)
    {
        Rooms = Array.AsReadOnly(rooms.OrderBy(r => r.Id).ToArray());
        Bookings = Array.AsReadOnly(bookings.OrderBy(b => b.Code, StringComparer.Ordinal).ToArray());
        if (Rooms.Select(r => r.Id).Distinct().Count() != Rooms.Count ||
            Bookings.Select(b => b.Code).Distinct(StringComparer.Ordinal).Count() != Bookings.Count)
            throw new InvalidOperationException("Duplicate snapshot identity.");
    }
    public bool HasSameData(PollingSnapshot other) => Rooms.SequenceEqual(other.Rooms) && Bookings.SequenceEqual(other.Bookings);
    internal static PollingSnapshot FromModels(IEnumerable<Room> rooms, IEnumerable<ReservationOverview> bookings) => new(
        rooms.Select(r => new RoomSnapshot(r.Id, r.RoomNumber, r.Type, r.PricePerNight, r.Capacity,
            r.Status, r.Description, r.ImagePath, r.DateAdded, r.DateDelete)),
        bookings.Select(b => new BookingSnapshot(b.ReservationCode, b.Guest, b.Room, b.Type,
            b.CheckInDate, b.CheckOutDate, b.Nights, b.Adults, b.Children,
            b.TotalAmount, b.AmountPaid, b.PaymentMethod, b.Status)));
}
