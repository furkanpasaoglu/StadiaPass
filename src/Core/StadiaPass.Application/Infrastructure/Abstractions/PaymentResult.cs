namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// The outcome of a charge. A decline is an answer, not an exception: the provider says no for a reason the
/// customer needs to read, so it is carried back as data rather than thrown from inside the adapter.
/// </summary>
public sealed record PaymentResult
{
    private PaymentResult()
    {
    }

    public bool IsSuccessful { get; private init; }

    /// <summary>The provider's own identifier for the charge, kept for reconciliation.</summary>
    public string? Reference { get; private init; }

    public string? FailureCode { get; private init; }

    public string? FailureMessage { get; private init; }

    public static PaymentResult Success(string reference) =>
        new() { IsSuccessful = true, Reference = reference };

    public static PaymentResult Failure(string code, string message) =>
        new() { IsSuccessful = false, FailureCode = code, FailureMessage = message };
}
