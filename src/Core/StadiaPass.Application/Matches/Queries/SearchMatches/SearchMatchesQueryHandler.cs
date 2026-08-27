using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Matches.Queries.SearchMatches;

/// <summary>
/// Search then fetch: Elasticsearch says which fixtures, PostgreSQL says what they are.
/// </summary>
/// <remarks>
/// <para>
/// The split is the point. The index is good at the question the database is bad at - which of these
/// fixtures does <c>besiktas</c> mean, and in what order of interest - and bad at the questions the database
/// answers for nothing: how many seats are free right now, is this match still on sale. So the index hands
/// back identifiers and no more, and every field that reaches the screen is read fresh. A visitor cannot see
/// a stale seat count here because there is no seat count here to be stale.
/// </para>
/// <para>
/// The order comes back from the index and has to be put back on by hand: relevance is the one thing the
/// database cannot recover, and <c>IN</c> returns rows in whatever order it likes.
/// </para>
/// </remarks>
internal sealed partial class SearchMatchesQueryHandler(
    IMatchSearchIndex searchIndex,
    IMatchRepository matchRepository,
    IDateTimeProvider dateTimeProvider,
    ILogger<SearchMatchesQueryHandler> logger)
    : IRequestHandler<SearchMatchesQuery, MatchSearchResultDto>
{
    /// <summary>
    /// Deep enough that a visitor sees everything worth seeing for a word, shallow enough that a one-letter
    /// search does not drag the whole catalogue back out of the database row by row.
    /// </summary>
    private const int MaxResults = 50;

    public async Task<MatchSearchResultDto> Handle(
        SearchMatchesQuery request,
        CancellationToken cancellationToken)
    {
        var term = request.Term.Trim();

        if (term.Length is 0)
        {
            return new MatchSearchResultDto(term, SearchAvailable: true, await ListAsync(cancellationToken));
        }

        IReadOnlyList<Guid> matchIds;

        try
        {
            matchIds = await searchIndex.SearchAsync(term, MaxResults, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A cluster that is down must cost the visitor the search box and nothing else. They get the
            // listing they would have got without typing, and a flag saying so - which is the honest answer,
            // and the one that keeps a search outage from looking like a catalogue that lost half its
            // fixtures.
            SearchFailed(logger, exception, term);

            return new MatchSearchResultDto(term, SearchAvailable: false, await ListAsync(cancellationToken));
        }

        var matches = await matchRepository.GetByIdsAsync(matchIds, cancellationToken);
        var byId = matches.ToDictionary(match => match.Id);

        // Identifiers the database did not answer for are dropped without a word: an index that still
        // remembers a match somebody deleted is behind, not broken, and the next reindex settles it.
        var found = matchIds
            .Where(byId.ContainsKey)
            .Select(matchId => byId[matchId].ToDto())
            .ToArray();

        return new MatchSearchResultDto(term, SearchAvailable: true, found);
    }

    private async Task<IReadOnlyList<MatchDto>> ListAsync(CancellationToken cancellationToken)
    {
        var matches = await matchRepository.GetUpcomingAsync(
            dateTimeProvider.UtcNow, categoryName: null, cancellationToken);

        return [.. matches.Select(match => match.ToDto())];
    }

    [LoggerMessage(
        EventId = 7100,
        Level = LogLevel.Warning,
        Message = "Match search for '{Term}' could not reach the index, so the full listing was returned "
            + "instead. Ticket sales are unaffected")]
    private static partial void SearchFailed(ILogger logger, Exception exception, string term);
}
