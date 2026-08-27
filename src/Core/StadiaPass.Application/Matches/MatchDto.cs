using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches;

public sealed record MatchDto(
    Guid Id,
    Guid CategoryId,
    string Category,
    Guid VenueId,
    string VenueName,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickOffUtc,
    string Status,
    int Capacity,
    int AvailableSeatCount,
    int ReservedSeatCount,
    int SoldSeatCount);

/// <summary>Seat map shaped the way a seat-picker draws it: block, then row, then seat.</summary>
public sealed record SeatMapDto(
    Guid MatchId,
    string Category,
    string VenueName,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickOffUtc,
    string Status,
    int Capacity,
    int AvailableSeatCount,
    IReadOnlyList<SeatBlockDto> Blocks);

public sealed record SeatBlockDto(string Block, int AvailableSeatCount, IReadOnlyList<SeatRowDto> Rows);

public sealed record SeatRowDto(int Row, IReadOnlyList<SeatDto> Seats);

public sealed record SeatDto(string SeatNumber, int Number, decimal Price, string Currency, string Status);

internal static class MatchMappings
{
    public static MatchDto ToDto(this Match match) => new(
        match.Id,
        match.CategoryId,
        match.CategoryName,
        match.VenueId,
        match.VenueName,
        match.HomeTeam,
        match.AwayTeam,
        match.KickOffUtc,
        match.Status.ToString(),
        match.Capacity,
        match.AvailableSeatCount,
        match.ReservedSeatCount,
        match.SoldSeatCount);

    /// <summary>
    /// The seat map counts its own seats rather than reading the match counters, and the two are not always
    /// the same number.
    /// </summary>
    /// <remarks>
    /// A hold that has run out is free to take: the seat renders white and a visitor can click it, because
    /// the transition refuses nobody once the deadline has passed. The counters do not know that yet - a
    /// lapsed hold is still <c>Reserved</c> in them until the sweeper comes round, up to a minute later.
    /// Taking the headline from <c>match.AvailableSeatCount</c> therefore put a number above the map that
    /// disagreed with the map, and with the block counts beside it, which were already counted this way.
    /// Every seat is already loaded here, so counting them is both free and the only answer that matches
    /// what the visitor is looking at. The listing keeps the counters, because it never loads a seat.
    /// </remarks>
    public static SeatMapDto ToSeatMapDto(this Match match, DateTimeOffset now)
    {
        var blocks = match.Seats
            .GroupBy(seat => seat.SeatNumber.Block, StringComparer.Ordinal)
            .OrderBy(block => block.Key, StringComparer.Ordinal)
            .Select(block => new SeatBlockDto(
                block.Key,
                block.Count(seat => seat.IsSelectableBy(string.Empty, now)),
                [.. block
                    .GroupBy(seat => seat.SeatNumber.Row)
                    .OrderBy(row => row.Key)
                    .Select(row => new SeatRowDto(
                        row.Key,
                        [.. row
                            .OrderBy(seat => seat.SeatNumber.Number)
                            .Select(seat => new SeatDto(
                                seat.SeatNumber.ToString(),
                                seat.SeatNumber.Number,
                                seat.Price.Amount,
                                seat.Price.Currency,
                                (seat.IsReservationExpired(now) ? SeatStatus.Available : seat.Status).ToString()))]))]))
            .ToArray();

        return new SeatMapDto(
            match.Id,
            match.CategoryName,
            match.VenueName,
            match.HomeTeam,
            match.AwayTeam,
            match.KickOffUtc,
            match.Status.ToString(),
            match.Capacity,
            blocks.Sum(block => block.AvailableSeatCount),
            blocks);
    }
}
