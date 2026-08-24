namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// The port the checkout talks to. Which adapter answers - the local mock or Stripe - is a configuration
/// decision made in Infrastructure; nothing in the application layer knows a provider name.
/// </summary>
public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default);
}
