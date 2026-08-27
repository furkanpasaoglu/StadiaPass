using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Matches.Search;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Infrastructure.Search;

/// <summary>
/// Asks both sides how many matches they think are on sale, so the gap between them is visible.
/// </summary>
/// <remarks>
/// <para>
/// This exists for the one search failure that makes no noise at all. A cluster that is down is loud - the
/// query throws, the handler logs it, the page says search is unavailable, the latency histogram fills up
/// with unreachable. An index that is <em>there</em> and <em>empty</em> is silent: every search succeeds,
/// every search finds nothing, and the site looks like a box office with no fixtures rather than a broken
/// one. A projection quietly falling behind - a consumer stuck in the error queue - looks the same, only
/// smaller.
/// </para>
/// <para>
/// Two counts on a timer, which is the same shape as the outbox measuring its own depth while its sweeper is
/// already at the table. The reads are cheap on both sides: Elasticsearch keeps the document count, and the
/// database counts the rows it would have listed anyway.
/// </para>
/// </remarks>
internal sealed partial class SearchIndexFreshnessWorker(
    IServiceScopeFactory scopeFactory,
    SearchMetrics metrics,
    IDateTimeProvider dateTimeProvider,
    ILogger<SearchIndexFreshnessWorker> logger) : BackgroundService
{
    /// <summary>
    /// Slow, because nothing here is urgent. A projection that is behind is behind for as long as it takes
    /// somebody to notice a graph, and asking twice a minute would only add load to make the same point.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await MeasureAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A measurement that fails is a measurement, not an outage. The gauges keep their last
                // values and the warning says why they stopped moving - taking the process down over an
                // unreadable graph would be a far worse trade.
                MeasurementFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task MeasureAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var searchIndex = scope.ServiceProvider.GetRequiredService<IMatchSearchIndex>();
        var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();

        var indexed = await searchIndex.CountAsync(cancellationToken);
        var indexable = await matchRepository.CountUpcomingAsync(dateTimeProvider.UtcNow, cancellationToken);

        metrics.RecordDepth(indexed, indexable);
    }

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Warning,
        Message = "The search index and the database could not be counted, so the freshness gauges are "
            + "holding their last values. Search itself is unaffected")]
    private static partial void MeasurementFailed(ILogger logger, Exception exception);
}
