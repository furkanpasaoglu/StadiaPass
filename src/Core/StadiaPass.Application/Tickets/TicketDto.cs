using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets;

public sealed record TicketDto(
    Guid Id,
    Guid MatchId,
    Guid MatchSeatId,
    string SeatNumber,
    decimal Price,
    string Currency,
    string HolderReference,
    string AccessCode,
    DateTimeOffset IssuedAtUtc,
    string Status);

/// <summary>Result of picking a seat: the seat is held for the caller until the deadline.</summary>
public sealed record SeatReservationDto(
    Guid MatchId,
    string SeatNumber,
    decimal Price,
    string Currency,
    string Status,
    DateTimeOffset ReservationExpiresAtUtc);

internal static class TicketMappings
{
    public static TicketDto ToDto(this Ticket ticket) => new(
        ticket.Id,
        ticket.MatchId,
        ticket.MatchSeatId,
        ticket.SeatNumber.ToString(),
        ticket.Price.Amount,
        ticket.Price.Currency,
        ticket.HolderReference,
        ticket.AccessCode,
        ticket.IssuedAtUtc,
        ticket.Status.ToString());

    public static SeatReservationDto ToReservationDto(this MatchSeat seat, Guid matchId) => new(
        matchId,
        seat.SeatNumber.ToString(),
        seat.Price.Amount,
        seat.Price.Currency,
        seat.Status.ToString(),
        seat.ReservationExpiresAtUtc!.Value);
}

/// <summary>
/// A ticket as its holder needs to see it: the seat and the code, and the fixture they are for.
/// </summary>
/// <remarks>
/// The fixture is joined on rather than left out, because a stub carrying a seat number and nothing else
/// cannot be told apart from the next one when somebody holds tickets to more than one match. It also
/// carries the fixture's own status, which is what lets the page say a match has been called off - and,
/// truthfully, that the money is on its way back - rather than showing a bare cancelled ticket and leaving
/// the holder to guess why.
/// </remarks>
public sealed record MyTicketDto(
    Guid Id,
    Guid MatchId,
    string SeatNumber,
    decimal Price,
    string Currency,
    string AccessCode,
    DateTimeOffset IssuedAtUtc,
    string Status,
    string HomeTeam,
    string AwayTeam,
    string VenueName,
    DateTimeOffset KickOffUtc,
    string MatchStatus);
