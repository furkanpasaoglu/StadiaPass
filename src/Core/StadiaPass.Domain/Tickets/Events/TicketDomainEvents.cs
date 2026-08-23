using StadiaPass.Domain.Common;

namespace StadiaPass.Domain.Tickets.Events;

public sealed record TicketIssuedDomainEvent(
    Guid TicketId,
    Guid MatchId,
    string SeatNumber,
    decimal Price,
    string HolderReference) : DomainEvent;

public sealed record TicketCancelledDomainEvent(
    Guid TicketId,
    Guid MatchId,
    Guid MatchSeatId,
    DateTimeOffset CancelledAtUtc) : DomainEvent;
