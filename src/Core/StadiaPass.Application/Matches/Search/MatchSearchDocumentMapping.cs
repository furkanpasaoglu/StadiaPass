using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches.Search;

/// <summary>
/// The one place a fixture is turned into a search document.
/// </summary>
/// <remarks>
/// Shared by the full rebuild and by the projection that keeps up with it one match at a time. Two spellings
/// of this would drift, and the way it would show is a fixture that reads differently depending on whether
/// it arrived through a reindex or through the broker - which is the kind of difference nobody goes looking
/// for until a search comes back wrong.
/// </remarks>
internal static class MatchSearchDocumentMapping
{
    /// <param name="city">
    /// Where the venue is. The match aggregate denormalises the venue's name but not its city, so the caller
    /// looks it up - and passes an empty string when there is nothing to look up, because a fixture whose
    /// venue has been deleted is still worth finding by the name of its teams.
    /// </param>
    public static MatchSearchDocument ToSearchDocument(this Match match, string city) =>
        new(
            match.Id,
            match.HomeTeam,
            match.AwayTeam,
            match.VenueName,
            city,
            match.CategoryName,
            match.KickOffUtc,
            match.Status.ToString());
}
