using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;

namespace StadiaPass.Application.Payments.Commands.IssueOwedRefund;

/// <summary>
/// The far side of an owed refund: one call to the provider, and no forgiveness if it does not work.
/// </summary>
/// <remarks>
/// Nothing here catches, on purpose. This is the second attempt at giving money back - the first one already
/// failed inside the checkout - so swallowing a failure here would put the money exactly where the whole
/// mechanism exists to stop it going: nowhere, quietly. Throwing hands it to the broker, which redelivers and
/// eventually parks it where somebody can see it.
/// </remarks>
internal sealed partial class IssueOwedRefundCommandHandler(
    IPaymentService paymentService,
    ILogger<IssueOwedRefundCommandHandler> logger) : IRequestHandler<IssueOwedRefundCommand>
{
    public async Task Handle(IssueOwedRefundCommand request, CancellationToken cancellationToken)
    {
        var refund = await paymentService.RefundPaymentAsync(
            request.PaymentTransactionId, request.Amount, cancellationToken);

        if (refund.IsSuccessful)
        {
            OwedRefundIssued(
                logger, request.Amount, request.PaymentTransactionId, refund.TransactionId!, request.Reason);

            return;
        }

        throw new PaymentFailedException(
            refund.FailureCode ?? "refund_failed",
            $"The provider would not refund {request.Amount} {request.Currency} of payment "
            + $"{request.PaymentTransactionId}: {refund.FailureMessage ?? "no reason given"}.");
    }

    [LoggerMessage(
        EventId = 3502,
        Level = LogLevel.Information,
        Message = "Refunded {Amount} of payment {PaymentTransactionId} as refund {RefundTransactionId} "
            + "({Reason}); the money owed for a sale that never happened is back")]
    private static partial void OwedRefundIssued(
        ILogger logger,
        decimal amount,
        string paymentTransactionId,
        string refundTransactionId,
        string reason);
}
