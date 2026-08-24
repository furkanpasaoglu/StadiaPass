using System.Diagnostics.CodeAnalysis;

namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// Turns a request from a payment provider into something this system will act on, or refuses it.
/// </summary>
/// <remarks>
/// The endpoint behind this is anonymous - a provider has no account here and no token to present - so the
/// signature is the whole of its security. Everything about verifying one is provider-specific, which is why
/// it lives behind a port: the endpoint hands over bytes and a header and is told yes or no, and never learns
/// whose bytes they were.
/// </remarks>
public interface IPaymentWebhookReader
{
    /// <summary>
    /// Verifies the signature and translates what it finds.
    /// </summary>
    /// <param name="payload">The body exactly as it arrived. A signature is over bytes, not over meaning.</param>
    /// <param name="signature">The provider's signature header.</param>
    /// <returns>
    /// <see langword="false"/> when the signature does not hold - the only correct answer to which is to
    /// refuse the request. A verified event whose type this system does not care about comes back
    /// <see langword="true"/> with no <see cref="PaymentWebhookEvent.Message"/>: genuine, and nothing to do.
    /// </returns>
    bool TryRead(
        string payload,
        string? signature,
        [NotNullWhen(true)] out PaymentWebhookEvent? webhookEvent);
}

/// <summary>
/// A verified provider event. <paramref name="Message"/> is one of the registered integration events, or
/// <see langword="null"/> for the many event types a provider sends that this system has no use for.
/// </summary>
public sealed record PaymentWebhookEvent(string ProviderEventId, string ProviderEventType, object? Message);
