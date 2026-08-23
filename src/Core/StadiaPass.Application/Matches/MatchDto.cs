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

    public static SeatMapDto ToSeatMapDto(this Match match, DateTimeOffset now) => new(
        match.Id,
        match.CategoryName,
        match.VenueName,
        match.HomeTeam,
        match.AwayTeam,
        match.KickOffUtc,
        match.Status.ToString(),
        match.Capacity,
        match.AvailableSeatCount,
        [.. match.Seats
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
                                (seat.IsReservationExpired(now) ? SeatStatus.Available : seat.Status).ToString()))]))]))]);
}
