namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// Where something a provider told us waits between arriving and being acted on.
/// </summary>
/// <remarks>
/// A webhook is a request from somebody who is not waiting politely. Stripe wants an answer in seconds and
/// retries anything slower, so the endpoint writes the event down, says yes, and lets a worker do the real
/// work afterwards - the same shape as the outbox, pointing the other way.
/// </remarks>
public interface IInbox
{
    /// <summary>
    /// Records the event, unless it is one already recorded.
    /// </summary>
    /// <param name="providerEventId">
    /// The provider's own id. A provider that guarantees at-least-once delivery will send the same event
    /// again, and this is what makes the second one a no-op instead of a second sale being undone.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when this event had already been received. Not an error: the right answer to
    /// a redelivery is the same 200 the first one got.
    /// </returns>
    Task<bool> TryRecordAsync(
        string providerEventId,
        string providerEventType,
        object message,
        CancellationToken cancellationToken = default);
}
