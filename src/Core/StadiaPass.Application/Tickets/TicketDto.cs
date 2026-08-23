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
