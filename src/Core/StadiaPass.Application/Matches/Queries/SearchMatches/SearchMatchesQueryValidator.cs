using FluentValidation;

namespace StadiaPass.Application.Matches.Queries.SearchMatches;

/// <summary>
/// A ceiling on what an anonymous caller may hand the cluster.
/// </summary>
/// <remarks>
/// The search endpoint is public, the way browsing is, and until now the term went to Elasticsearch at
/// whatever length it arrived. Nothing about a fixture is long: the longest thing anybody types here is a
/// club name and the venue it plays at. A term far past that is not somebody looking for a match, and it is
/// analysed, fuzzy-matched and prefix-matched per term per field before the cluster can decide it found
/// nothing - which is work an unauthenticated caller should not be able to ask for by the megabyte.
/// <para>
/// Refused rather than truncated: a search silently answering a different question than the one asked is the
/// kind of thing somebody debugs for an hour.
/// </para>
/// </remarks>
internal sealed class SearchMatchesQueryValidator : AbstractValidator<SearchMatchesQuery>
{
    /// <summary>Comfortably past "Fenerbahce Galatasaray Sukru Saracoglu Istanbul" and nowhere near abuse.</summary>
    private const int MaxTermLength = 100;

    public SearchMatchesQueryValidator()
    {
        RuleFor(query => query.Term)
            .MaximumLength(MaxTermLength)
            .WithMessage($"A search term cannot be longer than {MaxTermLength} characters.");
    }
}
