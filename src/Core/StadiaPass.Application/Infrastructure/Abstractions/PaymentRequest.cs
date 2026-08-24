using StadiaPass.Domain.Common.ValueObjects;

namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// What the application asks a payment provider to do. <paramref name="Reference"/> is stable for a given
/// seat of a given match, so a provider that understands idempotency will not charge twice when a customer
/// double-clicks or a retry replays the call.
/// <para>
/// <c>Correlation</c> is carried by the provider and handed back on every event about this payment. A
/// dispute months later knows only the charge; this is what turns it back into a seat.
/// </para>
/// </summary>
public sealed record PaymentRequest(
    Money Amount,
    PaymentCard Card,
    string Reference,
    string Description,
    IReadOnlyDictionary<string, string> Correlation);
