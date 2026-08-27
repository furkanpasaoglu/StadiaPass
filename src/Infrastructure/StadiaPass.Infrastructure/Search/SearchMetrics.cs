using System.Diagnostics.Metrics;

namespace StadiaPass.Infrastructure.Search;

/// <summary>
/// What the search box is actually doing, published for Prometheus to scrape.
/// </summary>
/// <remarks>
/// <para>
/// Search fails softly by design: a cluster that is down costs the visitor their search box and nothing
/// else, and the page says so and hands back the plain listing. That is the right behaviour and it is also
/// invisible - without these, an outage that lasted all night would leave one warning line per request in a
/// log nobody reads and not a mark on any graph.
/// </para>
/// <para>
/// One histogram carries three answers, which is why there is no separate counter for any of them: its
/// count is how many searches were run, its buckets are how long they took, and splitting that count by the
/// outcome tag is how many fell back. A counter beside it would be the same number kept twice.
/// </para>
/// </remarks>
internal sealed class SearchMetrics : IDisposable
{
    public const string MeterName = "StadiaPass.Search";

    /// <summary>The index answered. Whether it found anything is a different question.</summary>
    private const string Answered = "answered";

    /// <summary>The index could not be reached, so the visitor was given the listing instead.</summary>
    private const string Unavailable = "unavailable";

    /// <summary>
    /// Where to cut the latency histogram, in seconds.
    /// </summary>
    /// <remarks>
    /// Spelled out because the defaults are wrong here and wrong quietly. .NET buckets a histogram at
    /// 0, 5, 10, 25 ... 10000, which are sensible boundaries for milliseconds and useless for seconds: every
    /// real search - twenty milliseconds against a warm cluster - lands in the first bucket, and a p95 read
    /// off that comes back somewhere around five seconds. Confidently, and by two orders of magnitude. The
    /// unit stays seconds, because that is what Prometheus and every other duration on this dashboard use.
    /// <para>
    /// The top boundary is the two second ceiling the search request is given, so anything past it is a
    /// search that was abandoned rather than one that was slow.
    /// </para>
    /// </remarks>
    private static readonly double[] DurationBuckets =
        [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2];

    private readonly Meter _meter;
    private readonly Histogram<double> _duration;

    private long _indexed;
    private long _indexable;

    public SearchMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _duration = _meter.CreateHistogram<double>(
            "stadiapass.search.duration",
            unit: "s",
            description: "How long a search took, tagged with whether the index answered or was unreachable.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

        // The pair, rather than the difference between them. A lag of three says nothing on its own: three
        // out of four hundred is a projection catching up, and three out of three is an index somebody
        // dropped - and the second one is the failure worth seeing, because every search still succeeds and
        // simply finds nothing.
        _meter.CreateObservableGauge(
            "stadiapass.search.indexed_matches",
            () => Interlocked.Read(ref _indexed),
            unit: "{match}",
            description: "Documents in the search index.");

        _meter.CreateObservableGauge(
            "stadiapass.search.indexable_matches",
            () => Interlocked.Read(ref _indexable),
            unit: "{match}",
            description: "Matches the database says belong in it - upcoming and not cancelled.");
    }

    public void RecordAnswered(TimeSpan elapsed) =>
        _duration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("outcome", Answered));

    public void RecordUnavailable(TimeSpan elapsed) =>
        _duration.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("outcome", Unavailable));

    /// <summary>
    /// Read on a timer rather than in the gauge callbacks, which are synchronous and run on the collector's
    /// thread - the same reason the outbox counts where its sweeper already is.
    /// </summary>
    public void RecordDepth(long indexed, long indexable)
    {
        Interlocked.Exchange(ref _indexed, indexed);
        Interlocked.Exchange(ref _indexable, indexable);
    }

    public void Dispose() => _meter.Dispose();
}
