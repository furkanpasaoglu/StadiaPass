using MediatR;

namespace StadiaPass.Application.Matches.Queries.SearchMatches;

/// <summary>Free-text search over the fixtures on sale - teams, venue, city, sport.</summary>
public sealed record SearchMatchesQuery(string Term) : IRequest<MatchSearchResultDto>;

/// <summary>
/// The matches found, and whether they were actually found or merely listed.
/// </summary>
/// <param name="SearchAvailable">
/// <see langword="false"/> when the index could not be reached and the caller is looking at the plain
/// listing instead. Said out loud rather than hidden, because a screen that quietly ignores what somebody
/// typed and shows them everything is worse than one that admits it.
/// </param>
public sealed record MatchSearchResultDto(
    string Term,
    bool SearchAvailable,
    IReadOnlyList<MatchDto> Matches);
