using StadiaPass.Domain.Common;

namespace StadiaPass.Domain.Matches.Events;

public sealed record MatchScheduledDomainEvent(Guid MatchId, DateTimeOffset KickOffUtc) : DomainEvent;

public sealed record MatchPostponedDomainEvent(Guid MatchId, DateTimeOffset NewKickOffUtc) : DomainEvent;
