using MassTransit;
using MediatR;
using StadiaPass.Application.Payments.Commands.ReconcilePayment;
using StadiaPass.Application.Payments.Commands.VoidPaidTicket;
using StadiaPass.Application.Payments.Events;

namespace StadiaPass.Infrastructure.Messaging;

/// <summary>
/// The far side of the inbox. Each of these is deliberately three lines: the work is a use case and lives in
/// the application layer with the rest of them, so what happens when a payment is disputed is written where
/// somebody would look for it rather than in a transport adapter.
/// </summary>
/// <remarks>
/// Nothing here catches. A consumer that swallows is a consumer that has switched off the broker's redelivery
/// and its error queue, and these are events about money - the two things worth having.
/// <para>
/// The inbox already refused any event the provider sent twice, so these run once per event. They are still
/// written to be safe run twice, because a broker acknowledgement can be lost after the work was done: the
/// command they send looks for a live ticket and does nothing when there is not one.
/// </para>
/// </remarks>
internal sealed class PaymentSucceededConsumer(ISender sender) : IConsumer<PaymentSucceeded>
{
    public Task Consume(ConsumeContext<PaymentSucceeded> context) =>
        sender.Send(
            new ReconcilePaymentCommand(
                context.Message.PaymentIntentId,
                context.Message.MatchId,
                context.Message.SeatNumber,
                context.Message.HolderReference,
                context.Message.Amount,
                context.Message.Currency),
            context.CancellationToken);
}

/// <summary>A chargeback. The seat comes back even though the dispute may yet be won - see the contract.</summary>
internal sealed class PaymentDisputedConsumer(ISender sender) : IConsumer<PaymentDisputed>
{
    public Task Consume(ConsumeContext<PaymentDisputed> context) =>
        sender.Send(
            new VoidPaidTicketCommand(
                context.Message.PaymentIntentId,
                $"disputed: {context.Message.Reason ?? "no reason given"}"),
            context.CancellationToken);
}

/// <summary>
/// A refund. Either somebody pressed the button in the provider's dashboard, or this application's own
/// compensation is echoing back - the command tells them apart by whether there is a ticket.
/// </summary>
internal sealed class PaymentRefundedConsumer(ISender sender) : IConsumer<PaymentRefunded>
{
    public Task Consume(ConsumeContext<PaymentRefunded> context) =>
        sender.Send(
            new VoidPaidTicketCommand(context.Message.PaymentIntentId, "refunded"),
            context.CancellationToken);
}
