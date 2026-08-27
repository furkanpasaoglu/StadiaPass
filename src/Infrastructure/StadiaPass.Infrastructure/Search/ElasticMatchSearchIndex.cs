using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Bulk;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;
using StadiaPass.Application.Matches.Search;

namespace StadiaPass.Infrastructure.Search;

/// <summary>
/// The only place in the solution that knows Elasticsearch exists.
/// </summary>
/// <remarks>
/// There is no search service layer above this and no Elasticsearch type below it: the application layer
/// asks <see cref="IMatchSearchIndex"/> for identifiers, and everything about analyzers, relevance and
/// fuzziness stops here.
/// </remarks>
internal sealed class ElasticMatchSearchIndex(ElasticsearchClient client) : IMatchSearchIndex
{
    /// <summary>What a visitor waiting on a search box will spend before being handed the listing instead.</summary>
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Which fields a typed word is weighed against, and how heavily.
    /// </summary>
    /// <remarks>
    /// Somebody typing into a ticket search is naming a team far more often than a sport, so the two team
    /// names carry the most weight, the venue and the city rather less, and the sport least of all - it is
    /// what a category filter is for, and only here so that typing <c>basketbol</c> is not met with nothing.
    /// </remarks>
    private static readonly Fields SearchableFields = new[]
    {
        "homeTeam^4",
        "awayTeam^4",
        "venueName^2",
        "city^2",
        "category"
    };

    /// <summary>
    /// The same fields again, held without stopwords or stemming, for matching a word nobody has finished
    /// typing. See the index definition for why a half-typed word cannot be stemmed.
    /// </summary>
    private static readonly Fields PrefixFields = new[]
    {
        "homeTeam.prefix^4",
        "awayTeam.prefix^4",
        "venueName.prefix^2",
        "city.prefix^2",
        "category.prefix"
    };

    public async Task<IReadOnlyList<Guid>> SearchAsync(
        string term,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchRequest(MatchSearchIndex.Name)
        {
            Size = limit,

            // Tighter than the client's own ceiling, because this is the one call somebody is sitting in
            // front of waiting for. Falling back to the plain listing after two seconds is a search that
            // failed; doing it after five is a page that hung.
            RequestConfiguration = new RequestConfiguration { RequestTimeout = SearchTimeout },

            // The hit carries the identifier as its _id, so there is nothing in the document worth shipping
            // back over the wire - the rows are about to be read from the database anyway.
            Source = new SourceConfig(false),

            Query = new BoolQuery
            {
                // Two ways to match the same words, and a hit needs only one of them. Both score, so a
                // fixture that matches whole words and as a prefix outranks one that only did the latter.
                MinimumShouldMatch = new MinimumShouldMatch(1),

                Should =
                [
                    new MultiMatchQuery
                    {
                        Query = term,
                        Fields = SearchableFields,

                        // BestFields: the score is the best single field's, not the sum of all of them. A
                        // fixture is relevant because one name matched well, and adding up weak matches
                        // across five fields would let a vague hit outrank an exact one.
                        Type = TextQueryType.BestFields,

                        // One typo, or two in a long word, and the analyzer has already taken the accents
                        // out of the argument. Everything a visitor types is a proper noun they may have
                        // only heard, so this is the difference between finding Eczacibasi and not.
                        Fuzziness = new Fuzziness("AUTO")
                    },

                    new MultiMatchQuery
                    {
                        Query = term,

                        // The unstemmed copies, not the fields above. Stemming a half-typed word rewrites it
                        // rather than shortening it, and the prefix then matches nothing.
                        Fields = PrefixFields,

                        // Whole words match as whole words, and the last one a visitor typed matches as a
                        // prefix - which is what a search box is: somebody typing "fener" has not finished
                        // the word, and meeting them with nothing until they reach "fenerbahce" is a box
                        // most people give up on. No fuzziness on this one: a prefix already forgives the
                        // letters that are missing, and forgiving the ones that are there as well would turn
                        // three characters into a match for most of the catalogue.
                        Type = TextQueryType.BoolPrefix
                    }
                ],

                // Scored by the words, narrowed by the clock. A filter rather than another must, because
                // "has not kicked off" is not a matter of degree and there is no reason to score it - which
                // also lets Elasticsearch cache it. Resolved by the cluster, so a match does not survive in
                // the results because this process and that container disagree about the time.
                Filter =
                [
                    new DateRangeQuery("kickOffUtc") { Gte = DateMath.Now }
                ]
            }
        };

        var response = await client.SearchAsync<MatchSearchDocument>(request, cancellationToken);

        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException(
                $"Elasticsearch refused the match search: {response.DebugInformation}");
        }

        return [.. response.Hits.Select(hit => Guid.Parse(hit.Id!))];
    }

    public async Task IndexAsync(
        IReadOnlyCollection<MatchSearchDocument> documents,
        CancellationToken cancellationToken = default)
    {
        if (documents.Count is 0)
        {
            return;
        }

        var request = new BulkRequest(MatchSearchIndex.Name)
        {
            // Without this a document written here is not findable for about a second, which is exactly long
            // enough for a reindex to be followed by a search that returns nothing and looks like a bug.
            Refresh = Refresh.WaitFor,

            // Keyed by the match id rather than left to Elasticsearch, which is what makes writing the same
            // fixture twice an overwrite instead of a duplicate - and what lets a hit hand back an _id that
            // means something to the database.
            Operations =
            [
                .. documents.Select(document => new BulkIndexOperation<MatchSearchDocument>(document)
                {
                    Id = document.Id.ToString()
                })
            ]
        };

        var response = await client.BulkAsync(request, cancellationToken);

        if (!response.IsValidResponse || response.Errors)
        {
            throw new InvalidOperationException(
                $"Elasticsearch refused part of the match index write: {response.DebugInformation}");
        }
    }

    public async Task RecreateAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await client.Indices.DeleteAsync(MatchSearchIndex.Name, cancellationToken);

        // A 404 is the ordinary case on a cluster that has just come up with an empty data volume, so it is
        // not a failure - anything else is.
        if (!deleted.IsValidResponse && deleted.ApiCallDetails?.HttpStatusCode is not 404)
        {
            throw new InvalidOperationException(
                $"Elasticsearch refused to drop the match index: {deleted.DebugInformation}");
        }

        await SearchIndexInitializer.EnsureIndexAsync(client, cancellationToken);
    }
}
