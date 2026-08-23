using StadiaPass.Domain.Common;

namespace StadiaPass.Domain.Tickets.Events;

public sealed record TicketIssuedDomainEvent(Guid TicketId, Guid MatchId, string SeatNumber, decimal Price)
    : DomainEvent;

public sealed record TicketReservedDomainEvent(
    Guid TicketId,
    Guid MatchId,
    string HolderReference,
    DateTimeOffset ExpiresAtUtc) : DomainEvent;

public sealed record TicketSoldDomainEvent(
    Guid TicketId,
    Guid MatchId,
    string HolderReference,
    decimal Price,
    DateTimeOffset SoldAtUtc) : DomainEvent;

public sealed record TicketReservationReleasedDomainEvent(Guid TicketId, Guid MatchId, DateTimeOffset ReleasedAtUtc)
    : DomainEvent;
