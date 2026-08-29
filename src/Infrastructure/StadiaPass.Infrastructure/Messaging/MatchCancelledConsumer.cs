using MassTransit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Matches.Events;
using StadiaPass.Application.Payments.Commands.SettleCancelledTicket;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Infrastructure.Messaging;

/// <summary>
/// Pays back a cancelled fixture, one ticket at a time.
/// </summary>
/// <remarks>
/// <para>
/// The command that cancels a fixture only shuts the till; this is the half that costs money and time, which
/// is exactly why it is out here on the broker rather than inside that transaction. A sold-out fixture in a
/// large venue is hundreds of tickets, each owing a refund to a provider that can refuse, rate-limit or
/// simply be slow, and none of that belongs in a transaction holding the match row.
/// </para>
/// <para>
/// A scope per ticket, for the reason the expired-hold sweeper gives: settling them through one change
/// tracker means a ticket that loses a race leaves its modified entities behind, and the next ticket's save
/// carries them along and fails on a token that is now permanently stale - one unlucky seat quietly taking
/// the rest of the fixture down with it.
/// </para>
/// <para>
/// Safe to run again, which is what lets a half-finished pass simply be redelivered: each settlement is
/// addressed by its payment, and the lookup behind it only ever returns a ticket that is still live. A second
/// pass over a settled ticket finds nothing and writes nothing.
/// </para>
/// </remarks>
internal sealed partial class MatchCancelledConsumer(
    IServiceScopeFactory scopeFactory,
    ILogger<MatchCancelledConsumer> logger) : IConsumer<MatchCancelledEvent>
{
    public async Task Consume(ConsumeContext<MatchCancelledEvent> context)
    {
        var cancellationToken = context.CancellationToken;

        IReadOnlyList<string> payments;

        await using (var discovery = scopeFactory.CreateAsyncScope())
        {
            payments = await discovery.ServiceProvider
                .GetRequiredService<ITicketRepository>()
                .GetLivePaymentIntentsForMatchAsync(context.Message.MatchId, cancellationToken);
        }

        if (payments.Count is 0)
        {
            NothingToSettle(logger, context.Message.MatchId);

            return;
        }

        SettlementStarted(logger, payments.Count, context.Message.MatchId);

        var failed = 0;

        foreach (var paymentIntentId in payments)
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            try
            {
                await scope.ServiceProvider
                    .GetRequiredService<ISender>()
                    .Send(
                        new SettleCancelledTicketCommand(paymentIntentId, context.Message.Reason),
                        cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One ticket must not cost the other three hundred theirs, so the loop carries on and the
                // failure is counted rather than thrown from here.
                failed++;

                SettlementFailed(logger, paymentIntentId, context.Message.MatchId, exception);
            }
        }

        if (failed is not 0)
        {
            // Thrown once the rest are done, so the broker redelivers and the next pass retries only what is
            // still live. Everything settled by this pass is skipped, because its ticket is no longer live.
            throw new InvalidOperationException(
                $"{failed} of {payments.Count} tickets of cancelled match {context.Message.MatchId} could not "
                + "be settled and will be tried again.");
        }

        SettlementFinished(logger, payments.Count, context.Message.MatchId);
    }

    [LoggerMessage(
        EventId = 7300,
        Level = LogLevel.Information,
        Message = "Cancelled match {MatchId} had no tickets left to settle")]
    private static partial void NothingToSettle(ILogger logger, Guid matchId);

    [LoggerMessage(
        EventId = 7301,
        Level = LogLevel.Information,
        Message = "Settling {TicketCount} tickets of cancelled match {MatchId}")]
    private static partial void SettlementStarted(ILogger logger, int ticketCount, Guid matchId);

    [LoggerMessage(
        EventId = 7302,
        Level = LogLevel.Information,
        Message = "Settled {TicketCount} tickets of cancelled match {MatchId}; the refunds are on their way")]
    private static partial void SettlementFinished(ILogger logger, int ticketCount, Guid matchId);

    [LoggerMessage(
        EventId = 7303,
        Level = LogLevel.Warning,
        Message = "Payment {PaymentIntentId} of cancelled match {MatchId} could not be settled and will be "
            + "tried again")]
    private static partial void SettlementFailed(
        ILogger logger,
        string paymentIntentId,
        Guid matchId,
        Exception exception);
}
