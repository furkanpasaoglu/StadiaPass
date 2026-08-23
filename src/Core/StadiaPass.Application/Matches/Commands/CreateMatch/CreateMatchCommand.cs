using MediatR;
using StadiaPass.Domain.Common.ValueObjects;

namespace StadiaPass.Application.Matches.Commands.CreateMatch;

/// <summary>
/// Admin use case. Creating a match materialises the whole seat map of the venue as Available seats -
/// tickets are never issued up front.
/// </summary>
public sealed record CreateMatchCommand(
    Guid CategoryId,
    Guid VenueId,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickOffUtc,
    decimal BasePrice,
    string Currency = Money.DefaultCurrency) : IRequest<MatchDto>;
