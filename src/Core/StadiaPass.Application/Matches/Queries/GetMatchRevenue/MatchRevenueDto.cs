namespace StadiaPass.Application.Matches.Queries.GetMatchRevenue;

/// <summary>
/// One fixture's takings. Sold and refunded are reported side by side rather than as a single net figure,
/// because "we sold 900 and gave 300 back" and "we sold 600" are the same number and not the same afternoon.
/// </summary>
/// <param name="NetRevenue">
/// Live tickets only. A refunded ticket is money that came in and went back out, so it is not revenue - the
/// rule lives in the handler, never in a prompt.
/// </param>
/// <param name="RefundedAmount">What the refunded tickets came to, for the same period.</param>
/// <param name="OccupancyPercent">
/// Sold seats against capacity, from the fixture's own counters. Held seats are not sold and are not counted.
/// </param>
public sealed record MatchRevenueDto(
    Guid MatchId,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickOffUtc,
    string Status,
    string Currency,
    int TicketsSold,
    decimal NetRevenue,
    int TicketsRefunded,
    decimal RefundedAmount,
    int Capacity,
    int SeatsSold,
    decimal OccupancyPercent);
