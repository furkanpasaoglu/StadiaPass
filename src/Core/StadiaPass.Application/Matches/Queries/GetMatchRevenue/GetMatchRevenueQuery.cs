using MediatR;

namespace StadiaPass.Application.Matches.Queries.GetMatchRevenue;

/// <summary>What one fixture has taken, and how full it is.</summary>
public sealed record GetMatchRevenueQuery(Guid MatchId) : IRequest<MatchRevenueDto>;
