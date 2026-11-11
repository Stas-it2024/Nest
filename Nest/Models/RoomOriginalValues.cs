namespace Nest.Models;

// Exact SQL read values, not form/display values. Record equality is ordinal for strings,
// distinguishes NULL from empty and preserves decimal values and DateTime ticks.
public sealed record RoomOriginalValues(int Id, string RoomNumber, string Type,
    int Capacity, decimal PricePerNight, string Status, string? Description,
    string? ImagePath, DateTime? DateAdded, DateTime? DateDelete);
