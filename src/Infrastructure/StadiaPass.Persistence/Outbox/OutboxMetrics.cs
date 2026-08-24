using System.Diagnostics.Metrics;

namespace StadiaPass.Persistence.Outbox;

/// <summary>
/// How deep the outbox is, published for Prometheus to scrape.
/// </summary>
/// <remarks>
/// This is the single most useful number about the whole messaging path. A broker that is down, a consumer
/// that is broken, a sweeper that has stopped - all three show up the same way, as a pending count that
/// climbs and does not come back down. Without it the only evidence is a log line nobody is reading.
/// <para>
/// The gauges read a cached number rather than the database. An observable gauge's callback is synchronous
/// and runs on the collector's thread, so a query in there would block metric collection on however long
/// PostgreSQL felt like taking. The sweeper is already at the table every five seconds and counts while it
/// is there.
/// </para>
/// </remarks>
internal sealed class OutboxMetrics : IDisposable
{
    public const string MeterName = "StadiaPass.Outbox";

    private readonly Meter _meter;

    private long _pending;
    private long _dead;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _meter.CreateObservableGauge(
            "stadiapass.outbox.pending",
            () => Interlocked.Read(ref _pending),
            unit: "{message}",
            description: "Messages written but not yet taken by the broker.");

        _meter.CreateObservableGauge(
            "stadiapass.outbox.dead",
            () => Interlocked.Read(ref _dead),
            unit: "{message}",
            description: "Messages the sweeper has given up on. Anything above zero is waiting for a person.");
    }

    public void Record(long pending, long dead)
    {
        Interlocked.Exchange(ref _pending, pending);
        Interlocked.Exchange(ref _dead, dead);
    }

    public void Dispose() => _meter.Dispose();
}
