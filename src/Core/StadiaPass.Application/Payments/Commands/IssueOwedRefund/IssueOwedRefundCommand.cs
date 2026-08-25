using MediatR;

namespace StadiaPass.Application.Payments.Commands.IssueOwedRefund;

/// <summary>
/// Gives back money the checkout took for a sale that never committed.
/// </summary>
/// <remarks>
/// Safe to run more than once: the provider adapter keys the refund on the payment, so a second delivery
/// returns the first refund rather than sending the money twice.
/// </remarks>
public sealed record IssueOwedRefundCommand(
    string PaymentTransactionId,
    decimal Amount,
    string Currency,
    string Reason) : IRequest;
