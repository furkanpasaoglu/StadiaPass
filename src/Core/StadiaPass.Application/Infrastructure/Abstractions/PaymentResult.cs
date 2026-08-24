namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// The outcome of a charge or a refund. A decline is an answer, not an exception: the provider says no for a
/// reason the customer needs to read, so it is carried back as data rather than thrown from inside the
/// adapter. A refund that fails is the opposite case - nobody is waiting to read it, but somebody has to be
/// able to find it - which is why the same shape carries both.
/// </summary>
public sealed record PaymentResult
{
    private PaymentResult()
    {
    }

    public bool IsSuccessful { get; private init; }

    /// <summary>
    /// The provider's own identifier - a Stripe PaymentIntent id for a charge, a Refund id for a refund.
    /// It is the only thread back to the money, so it is what makes a row reconcilable by hand.
    /// </summary>
    public string? TransactionId { get; private init; }

    public string? FailureCode { get; private init; }

    public string? FailureMessage { get; private init; }

    public static PaymentResult Success(string transactionId) =>
        new() { IsSuccessful = true, TransactionId = transactionId };

    public static PaymentResult Failure(string code, string message) =>
        new() { IsSuccessful = false, FailureCode = code, FailureMessage = message };
}
