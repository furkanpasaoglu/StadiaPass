using MediatR;

namespace StadiaPass.Application.Matches.Queries.GetUpcomingMatches;

public sealed record GetUpcomingMatchesQuery : IRequest<IReadOnlyList<MatchDto>>;
