using System.Diagnostics.Metrics;

namespace StadiaPass.Persistence.Inbox;

/// <summary>
/// How deep the inbox is, published for Prometheus to scrape. The outbox's gauges pointing the other way.
/// </summary>
/// <remarks>
/// The dead count is the one that matters here, and it matters more than the outbox's. A message this system
/// failed to send is still its own to send again; a message the <em>provider</em> sent is one we answered
/// <c>200</c> to. Stripe has been told we have it and will never send it again, so an inbox row that has been
/// set aside is a chargeback nobody applied or a payment nobody reconciled, and the only remaining evidence
/// that it happened is the row itself.
/// <para>
/// Same shape as the outbox for the same reason: the gauges read a cached number rather than the database,
/// because an observable gauge's callback is synchronous and a query in there would block metric collection
/// on however long PostgreSQL felt like taking. The sweeper is already at the table every five seconds.
/// </para>
/// </remarks>
internal sealed class InboxMetrics : IDisposable
{
    public const string MeterName = "StadiaPass.Inbox";

    private readonly Meter _meter;

    private long _pending;
    private long _dead;

    public InboxMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _meter.CreateObservableGauge(
            "stadiapass.inbox.pending",
            () => Interlocked.Read(ref _pending),
            unit: "{message}",
            description: "Provider events recorded but not yet put on the bus.");

        _meter.CreateObservableGauge(
            "stadiapass.inbox.dead",
            () => Interlocked.Read(ref _dead),
            unit: "{message}",
            description: "Provider events the sweeper has given up on. The provider will not send them "
                + "again, so anything above zero is waiting for a person.");
    }

    public void Record(long pending, long dead)
    {
        Interlocked.Exchange(ref _pending, pending);
        Interlocked.Exchange(ref _dead, dead);
    }

    public void Dispose() => _meter.Dispose();
}
