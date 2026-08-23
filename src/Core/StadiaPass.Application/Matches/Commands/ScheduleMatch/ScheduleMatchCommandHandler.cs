using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches.Commands.ScheduleMatch;

internal sealed class ScheduleMatchCommandHandler(
    IMatchRepository matchRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<ScheduleMatchCommand, MatchDto>
{
    public async Task<MatchDto> Handle(ScheduleMatchCommand request, CancellationToken cancellationToken)
    {
        var match = Match.Schedule(
            request.HomeTeam,
            request.AwayTeam,
            request.Stadium,
            request.KickOffUtc,
            request.Capacity,
            dateTimeProvider.UtcNow);

        match.OpenSales();

        await matchRepository.AddAsync(match, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return match.ToDto();
    }
}
