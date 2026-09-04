namespace StadiaPass.McpServer.Api;

// Wire-shape mirrors of the WebAPI's public catalogue responses, the same way the MVC portal keeps its
// own copies: the MCP server is a client of the API like any other, and referencing the Application
// assembly for its DTOs would hand it a dependency on the whole write side it must never touch.

public sealed record MatchSummary(
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

public sealed record MatchSearchResult(
    string Term,
    bool SearchAvailable,
    IReadOnlyList<MatchSummary> Matches);

public sealed record SeatMap(
    Guid MatchId,
    string Category,
    string VenueName,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickOffUtc,
    string Status,
    int Capacity,
    int AvailableSeatCount,
    IReadOnlyList<SeatBlock> Blocks);

public sealed record SeatBlock(string Block, int AvailableSeatCount, IReadOnlyList<SeatRow> Rows);

public sealed record SeatRow(int Row, IReadOnlyList<Seat> Seats);

public sealed record Seat(string SeatNumber, int Number, decimal Price, string Currency, string Status);

/// <summary>
/// What one fixture has taken. Sold and refunded arrive separately, because they answer different
/// questions and a single net figure would let either of them be guessed at.
/// </summary>
public sealed record MatchRevenue(
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
