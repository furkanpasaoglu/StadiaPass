using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Matches.Commands.IndexMatch;

/// <summary>
/// Re-reads the fixture the message named and writes it into the index.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is caught here. This runs on a consumer, and a consumer that swallows has switched off both the
/// broker's redelivery and its error queue - which for a projection means an index that quietly stops
/// keeping up with a cluster that was only briefly unreachable. Throwing is what gets it tried again.
/// </para>
/// <para>
/// That is the opposite of what the search query does with the same failure, and deliberately so: a visitor
/// waiting on a page is owed an answer now, and a background projection is owed a retry.
/// </para>
/// </remarks>
internal sealed partial class IndexMatchCommandHandler(
    IMatchSearchIndex searchIndex,
    IMatchRepository matchRepository,
    IVenueRepository venueRepository,
    ILogger<IndexMatchCommandHandler> logger) : IRequestHandler<IndexMatchCommand>
{
    public async Task Handle(IndexMatchCommand request, CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetByIdAsync(request.MatchId, cancellationToken);

        if (match is null)
        {
            // The message outlived the fixture. Not an error and not worth retrying - the next full rebuild
            // is what takes the leftover document out of the index.
            MatchGone(logger, request.MatchId);

            return;
        }

        var venue = await venueRepository.GetByIdAsync(match.VenueId, cancellationToken);

        await searchIndex.IndexAsync([match.ToSearchDocument(venue?.City ?? string.Empty)], cancellationToken);

        Indexed(logger, match.Id);
    }

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Information,
        Message = "Match {MatchId} was written to the search index")]
    private static partial void Indexed(ILogger logger, Guid matchId);

    [LoggerMessage(
        EventId = 7103,
        Level = LogLevel.Information,
        Message = "Match {MatchId} was asked to be indexed but no longer exists, so nothing was written")]
    private static partial void MatchGone(ILogger logger, Guid matchId);
}
