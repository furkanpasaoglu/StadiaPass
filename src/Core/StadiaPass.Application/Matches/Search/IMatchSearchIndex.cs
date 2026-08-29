namespace StadiaPass.Application.Matches.Search;

/// <summary>
/// What a fixture looks like to the search engine: enough to be found by, and nothing to be trusted for.
/// </summary>
/// <remarks>
/// No counters, and that is the whole design. Free, held and sold move on every click somewhere in the
/// building; keeping them here would mean rewriting this document on every hold and every release, for
/// numbers the caller reads out of PostgreSQL a moment later anyway. What is here is what a fixture is
/// called and when it is played, which changes when somebody schedules or postpones a match and not
/// otherwise.
/// </remarks>
public sealed record MatchSearchDocument(
    Guid Id,
    string HomeTeam,
    string AwayTeam,
    string VenueName,
    string City,
    string Category,
    DateTimeOffset KickOffUtc,
    string Status);

/// <summary>
/// The search index, as the application layer is allowed to know it: words in, identifiers out.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a read model. The index is asked which fixtures match what somebody typed, and answers
/// with identifiers in relevance order; the rows themselves are then read from the database. Search then
/// fetch costs one extra round trip and buys back the thing a read model gives up - a listing that is never
/// stale, never disagrees with the seat map, and does not need the index to be right about anything except
/// which fixtures are interesting.
/// </para>
/// <para>
/// Everything here can throw. The index is a convenience laid over a system that sells tickets perfectly
/// well without it, so a caller is expected to catch and carry on rather than turn a search outage into a
/// site outage.
/// </para>
/// </remarks>
public interface IMatchSearchIndex
{
    /// <summary>
    /// The fixtures matching <paramref name="term"/>, most relevant first, kick-off still ahead of them.
    /// </summary>
    /// <returns>
    /// Identifiers only. They are not promised to exist any more - the index is a projection and can be
    /// behind - so the caller reads the rows and works with what came back.
    /// </returns>
    Task<IReadOnlyList<Guid>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many documents the index is holding.
    /// </summary>
    /// <remarks>
    /// Only ever asked for the sake of the graph beside it. On its own the number says nothing; against the
    /// count of matches the database thinks belong in the index it is the one thing that catches an index
    /// somebody dropped - because every search still succeeds afterwards and simply finds nothing, which
    /// looks exactly like a catalogue with nothing in it.
    /// </remarks>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes documents into the index, replacing any already there under the same identifier.</summary>
    Task IndexAsync(
        IReadOnlyCollection<MatchSearchDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a fixture out of the index.
    /// </summary>
    /// <remarks>
    /// A fixture that has been called off has to leave, and leaving is not something writing a document can
    /// do: the full rebuild drops what it does not select, but the projection that keeps up between rebuilds
    /// only ever wrote, so a cancelled match stayed findable and its link kept working. Asking for a document
    /// that is not there is not a failure - the rebuild may already have dropped it.
    /// </remarks>
    Task DeleteAsync(Guid matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws the index away and builds an empty one from the current definition.
    /// </summary>
    /// <remarks>
    /// An analyzer is applied when a document is written, so changing one cannot be applied to documents
    /// already written - the index has to be built again. PostgreSQL is the source of truth and this thing
    /// is derived from it, which is what makes that an ordinary operation rather than a data loss.
    /// </remarks>
    Task RecreateAsync(CancellationToken cancellationToken = default);
}
