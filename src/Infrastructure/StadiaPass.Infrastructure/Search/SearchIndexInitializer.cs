using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace StadiaPass.Infrastructure.Search;

/// <summary>
/// Puts the match index there if it is not, the way <c>DatabaseInitializer</c> does for the schema.
/// </summary>
/// <remarks>
/// <para>
/// Mappings cannot be changed on a live index - an analyzer is applied when a document is written, so
/// changing one means writing every document again. This only ever creates what is missing; changing the
/// definition in <see cref="MatchSearchIndex"/> means dropping the index and reindexing, which is what the
/// reindex command is for.
/// </para>
/// <para>
/// Failure here is logged and swallowed. Search is the only thing in this application that Elasticsearch
/// answers for: a cluster that will not come up must cost the visitor the search box, not the listing, the
/// seat map or the checkout - so it must not stop the API from starting either.
/// </para>
/// </remarks>
internal sealed partial class SearchIndexInitializer(
    ElasticsearchClient client,
    ILogger<SearchIndexInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (await EnsureIndexAsync(client, stoppingToken))
            {
                IndexReady(logger, MatchSearchIndex.Name);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            IndexUnavailable(logger, exception, MatchSearchIndex.Name);
        }
    }

    /// <summary>Creates the index from its definition unless it is already there.</summary>
    /// <returns><see langword="true"/> when the index exists afterwards.</returns>
    public static async Task<bool> EnsureIndexAsync(
        ElasticsearchClient client,
        CancellationToken cancellationToken)
    {
        var exists = await client.Indices.ExistsAsync(MatchSearchIndex.Name, cancellationToken);

        if (exists.Exists)
        {
            return true;
        }

        // The low-level way in, because the definition is JSON and stays JSON. Anything the response says
        // about a refusal is in DebugInformation, which is what the caller logs.
        var path = new EndpointPath(HttpMethod.PUT, MatchSearchIndex.Name);

        var created = await client.Transport.RequestAsync<StringResponse>(
            in path,
            PostData.String(MatchSearchIndex.Definition),
            null,
            null,
            cancellationToken);

        if (created.ApiCallDetails.HasSuccessfulStatusCode)
        {
            return true;
        }

        throw new InvalidOperationException(
            $"Elasticsearch refused to create the {MatchSearchIndex.Name} index: "
            + created.ApiCallDetails.DebugInformation);
    }

    [LoggerMessage(
        EventId = 7000,
        Level = LogLevel.Information,
        Message = "Elasticsearch index {IndexName} is ready")]
    private static partial void IndexReady(ILogger logger, string indexName);

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Warning,
        Message = "Elasticsearch index {IndexName} could not be prepared, so match search is unavailable. "
            + "Everything else - the listing, the seat map and checkout - is unaffected")]
    private static partial void IndexUnavailable(ILogger logger, Exception exception, string indexName);
}
