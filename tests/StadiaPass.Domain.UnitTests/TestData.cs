using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Domain.UnitTests;

/// <summary>
/// Builders for the aggregates under test. Every test states only the part of the world it cares about and
/// leaves the rest to these defaults, so a rule change shows up as one failing assertion rather than a wall
/// of broken setup.
/// </summary>
internal static class TestData
{
    /// <summary>Fixed clock: a test that reasons about a hold window must not race the wall clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset KickOff = Now.AddDays(30);

    public const string Holder = "3f6c9f7a-1f0e-4a1b-9d2f-2f6f9b1c7a41";

    public static SportCategory Football() =>
        SportCategory.Define("Football", "Eleven a side on grass", [VenueKind.Stadium]);

    public static SportCategory Basketball() =>
        SportCategory.Define("Basketball", "Indoor court", [VenueKind.Arena, VenueKind.Hall]);

    public static Venue Stadium(params BlockLayout[] blocks) =>
        Venue.Define(
            "Sukru Saracoglu",
            "Istanbul",
            VenueKind.Stadium,
            blocks.Length is 0 ? [new BlockLayout("MARATON", RowCount: 2, SeatsPerRow: 3)] : blocks);

    public static Venue Arena(params BlockLayout[] blocks) =>
        Venue.Define(
            "Sinan Erdem Spor Salonu",
            "Istanbul",
            VenueKind.Arena,
            blocks.Length is 0 ? [new BlockLayout("KUZEY", RowCount: 2, SeatsPerRow: 3)] : blocks);

    public static Match FootballMatch(Venue? venue = null, decimal basePrice = 100m) =>
        Match.Create(
            Football(),
            venue ?? Stadium(),
            "Fenerbahce",
            "Galatasaray",
            KickOff,
            Money.Create(basePrice),
            Now);

    public static MatchSeat SeatOf(Match match, string seatNumber) =>
        match.Seats.Single(seat => seat.SeatNumber.ToString() == seatNumber);
}
