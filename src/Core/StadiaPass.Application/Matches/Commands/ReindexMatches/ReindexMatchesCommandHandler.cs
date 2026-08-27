using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches.Commands.ReindexMatches;

/// <summary>
/// Reads every fixture still ahead of us and writes the lot into a freshly created index.
/// </summary>
/// <remarks>
/// <para>
/// Only upcoming fixtures go in. A match that has kicked off cannot be bought, so indexing it would put
/// results in front of a visitor that they can do nothing with, and would grow the index for ever besides.
/// Dropping and rebuilding is also how a match falls out of it: nothing has to remember to delete anything.
/// </para>
/// <para>
/// City is the one field a match does not carry - the aggregate denormalises the venue's name but not where
/// it is - so the venues are read once and joined in memory. There are as many venues as a company owns
/// buildings, which is why this is a dictionary rather than a join.
/// </para>
/// </remarks>
internal sealed partial class ReindexMatchesCommandHandler(
    IMatchSearchIndex searchIndex,
    IMatchRepository matchRepository,
    IVenueRepository venueRepository,
    IDateTimeProvider dateTimeProvider,
    ILogger<ReindexMatchesCommandHandler> logger)
    : IRequestHandler<ReindexMatchesCommand, ReindexMatchesResultDto>
{
    public async Task<ReindexMatchesResultDto> Handle(
        ReindexMatchesCommand request,
        CancellationToken cancellationToken)
    {
        var matches = await matchRepository.GetUpcomingAsync(
            dateTimeProvider.UtcNow, categoryName: null, cancellationToken);

        var venues = await venueRepository.GetAllAsync(cancellationToken);
        var cityByVenueId = venues.ToDictionary(venue => venue.Id, venue => venue.City);

        var documents = matches
            .Select(match => match.ToSearchDocument(cityByVenueId.GetValueOrDefault(match.VenueId, string.Empty)))
            .ToArray();

        // Recreated before anything is written, so a fixture that has been cancelled or has kicked off since
        // the last run is gone rather than lingering as a document nothing will ever overwrite.
        await searchIndex.RecreateAsync(cancellationToken);
        await searchIndex.IndexAsync(documents, cancellationToken);

        Reindexed(logger, documents.Length);

        return new ReindexMatchesResultDto(documents.Length);
    }

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Information,
        Message = "Rebuilt the match search index with {MatchCount} upcoming matches")]
    private static partial void Reindexed(ILogger logger, int matchCount);
}
