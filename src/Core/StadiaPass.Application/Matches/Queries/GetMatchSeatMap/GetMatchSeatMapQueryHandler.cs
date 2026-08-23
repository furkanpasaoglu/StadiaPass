using MediatR;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches.Queries.GetMatchSeatMap;

internal sealed class GetMatchSeatMapQueryHandler(
    IMatchRepository matchRepository,
    IDateTimeProvider dateTimeProvider) : IRequestHandler<GetMatchSeatMapQuery, SeatMapDto>
{
    public async Task<SeatMapDto> Handle(GetMatchSeatMapQuery request, CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetWithSeatMapAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        return match.ToSeatMapDto(dateTimeProvider.UtcNow);
    }
}
