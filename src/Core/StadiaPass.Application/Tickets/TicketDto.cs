using StadiaPass.Domain.Tickets;

namespace StadiaPass.Application.Tickets;

public sealed record TicketDto(
    Guid Id,
    Guid MatchId,
    string SeatNumber,
    decimal Price,
    string Currency,
    string Status,
    string? HolderReference,
    DateTimeOffset? ReservationExpiresAtUtc,
    DateTimeOffset? SoldAtUtc);

internal static class TicketMappings
{
    public static TicketDto ToDto(this Ticket ticket) => new(
        ticket.Id,
        ticket.MatchId,
        ticket.SeatNumber.ToString(),
        ticket.Price.Amount,
        ticket.Price.Currency,
        ticket.Status.ToString(),
        ticket.HolderReference,
        ticket.ReservationExpiresAtUtc,
        ticket.SoldAtUtc);
}
