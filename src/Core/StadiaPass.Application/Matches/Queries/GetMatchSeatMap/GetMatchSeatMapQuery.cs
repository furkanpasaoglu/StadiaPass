using MediatR;

namespace StadiaPass.Application.Matches.Queries.GetMatchSeatMap;

/// <summary>Feeds the interactive seat picker: every seat of the match with its current status.</summary>
public sealed record GetMatchSeatMapQuery(Guid MatchId) : IRequest<SeatMapDto>;
