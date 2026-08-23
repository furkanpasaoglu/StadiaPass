using MediatR;

namespace StadiaPass.Application.Matches.Commands.ScheduleMatch;

public sealed record ScheduleMatchCommand(
    string HomeTeam,
    string AwayTeam,
    string Stadium,
    DateTimeOffset KickOffUtc,
    int Capacity) : IRequest<MatchDto>;
