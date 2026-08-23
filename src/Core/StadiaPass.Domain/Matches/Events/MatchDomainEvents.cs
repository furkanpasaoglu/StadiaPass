using StadiaPass.Domain.Common;

namespace StadiaPass.Domain.Matches.Events;

public sealed record MatchCreatedDomainEvent(Guid MatchId, string Category, Guid VenueId, int Capacity)
    : DomainEvent;

public sealed record MatchPostponedDomainEvent(Guid MatchId, DateTimeOffset NewKickOffUtc) : DomainEvent;

public sealed record SeatReservedDomainEvent(
    Guid MatchId,
    Guid SeatId,
    string SeatNumber,
    string HolderReference,
    DateTimeOffset ExpiresAtUtc) : DomainEvent;

public sealed record SeatSoldDomainEvent(
    Guid MatchId,
    Guid SeatId,
    string SeatNumber,
    string HolderReference,
    decimal Price) : DomainEvent;

public sealed record SeatReleasedDomainEvent(Guid MatchId, Guid SeatId, string SeatNumber, DateTimeOffset ReleasedAtUtc)
    : DomainEvent;
