using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Common.ValueObjects;
using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.UnitTests;

/// <summary>
/// Real aggregates, mocked ports. A handler test that stubbed the domain would prove nothing about the rule
/// it is supposed to enforce, so the match is built for real and only the repository, the clock, the
/// unit of work and the caller are substituted.
/// </summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Shaped like a Keycloak subject, because that is what ICurrentUser hands the handler.</summary>
    public const string CurrentUserId = "3f6c9f7a-1f0e-4a1b-9d2f-2f6f9b1c7a41";

    public const string OtherUserId = "9c1f2b44-0f4a-4e6b-8a7e-11c8d5b6e3aa";

    public const string SeatNumber = "MARATON-1-1";

    public static MatchSeat SeatOf(Match match, string seatNumber) =>
        match.Seats.Single(seat => seat.SeatNumber.ToString() == seatNumber);

    public static Match FootballMatch() => FootballMatch("Fenerbahce");

    /// <summary>The same fixture under a different home team, for tests that must tell two of them apart.</summary>
    public static Match FootballMatch(string homeTeam) =>
        Match.Create(
            SportCategory.Define("Football", null, [VenueKind.Stadium]),
            Venue.Define(
                "Sukru Saracoglu",
                "Istanbul",
                VenueKind.Stadium,
                [new BlockLayout("MARATON", RowCount: 2, SeatsPerRow: 3)]),
            homeTeam,
            "Galatasaray",
            Now.AddDays(30),
            Money.Create(100m),
            Now);
}
