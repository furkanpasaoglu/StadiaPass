using MediatR;

namespace StadiaPass.Application.Matches.Queries.GetUpcomingMatches;

/// <summary>Customer facing listing, optionally narrowed to a single sport category.</summary>
public sealed record GetUpcomingMatchesQuery(string? Category = null) : IRequest<IReadOnlyList<MatchDto>>;
