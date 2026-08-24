using StadiaPass.Domain.Common.ValueObjects;

namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// What the application asks a payment provider to do. <paramref name="Reference"/> is stable for a given
/// seat of a given match, so a provider that understands idempotency will not charge twice when a customer
/// double-clicks or a retry replays the call.
/// </summary>
public sealed record PaymentRequest(
    Money Amount,
    PaymentCard Card,
    string Reference,
    string Description);
