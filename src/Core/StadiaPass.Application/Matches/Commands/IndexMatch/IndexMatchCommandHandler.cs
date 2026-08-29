using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches.Commands.IndexMatch;

/// <summary>
/// Re-reads the fixture the message named and makes the index agree with it - by writing the document, or by
/// taking it out.
/// </summary>
/// <remarks>
/// <para>
/// What belongs in the index is defined once, by the listing query: a fixture still ahead of us that has not
/// been called off. This applies the same rule one fixture at a time, so the listing, the full rebuild and
/// this projection cannot disagree about which matches a visitor should be offered. Without the removal half
/// they did disagree: a cancelled fixture stayed findable, and its link went on opening a seat map, until
/// somebody happened to rebuild the whole index.
/// </para>
/// <para>
/// Nothing is caught here. This runs on a consumer, and a consumer that swallows has switched off both the
/// broker's redelivery and its error queue - which for a projection means an index that quietly stops keeping
/// up with a cluster that was only briefly unreachable. Throwing is what gets it tried again.
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
    IDateTimeProvider dateTimeProvider,
    ILogger<IndexMatchCommandHandler> logger) : IRequestHandler<IndexMatchCommand>
{
    public async Task Handle(IndexMatchCommand request, CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetByIdAsync(request.MatchId, cancellationToken);

        if (match is null)
        {
            // The message outlived the fixture. Deleting rather than shrugging: leaving the document for the
            // next full rebuild leaves it findable in the meantime, and asking for a document that is not
            // there is not a failure.
            await searchIndex.DeleteAsync(request.MatchId, cancellationToken);

            MatchGone(logger, request.MatchId);

            return;
        }

        // The listing's own test, spelled the same way round: it keeps a fixture whose kick-off is still to
        // come and which is not cancelled. Anything else is taken out.
        if (match.Status is MatchStatus.Cancelled || match.KickOffUtc < dateTimeProvider.UtcNow)
        {
            await searchIndex.DeleteAsync(match.Id, cancellationToken);

            MatchWithdrawn(logger, match.Id, match.Status);

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
        Message = "Match {MatchId} was asked to be indexed but no longer exists, so it was taken out of the "
            + "index instead")]
    private static partial void MatchGone(ILogger logger, Guid matchId);

    [LoggerMessage(
        EventId = 7104,
        Level = LogLevel.Information,
        Message = "Match {MatchId} is {Status} or already played, so it was taken out of the search index")]
    private static partial void MatchWithdrawn(ILogger logger, Guid matchId, MatchStatus status);
}
