namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// The port the checkout talks to. Which adapter answers - the local mock or Stripe - is a configuration
/// decision made in Infrastructure; nothing in the application layer knows a provider name.
/// </summary>
public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the money back. This exists because taking payment and writing the sale cannot be made one
    /// atomic act: the charge succeeds against a provider, and the write that follows can still lose a race
    /// for the seat. Without a way to undo the charge, that customer is left paid up and seatless.
    /// </summary>
    /// <param name="transactionId">
    /// The provider's identifier from the <see cref="PaymentResult.TransactionId"/> of the original charge.
    /// </param>
    /// <param name="amount">
    /// How much to give back, in the currency the charge was made in - a provider refunds against the
    /// original transaction, so the currency is already settled and cannot be chosen here.
    /// </param>
    Task<PaymentResult> RefundPaymentAsync(
        string transactionId,
        decimal amount,
        CancellationToken cancellationToken = default);
}
