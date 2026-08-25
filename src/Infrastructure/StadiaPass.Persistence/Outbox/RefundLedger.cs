using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Payments.Events;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Persistence.Outbox;

/// <summary>
/// Writes an owed refund to the outbox on a unit of work of its own.
/// </summary>
/// <remarks>
/// The scope is the reason this class exists. Its caller is holding a change tracker full of a sale that has
/// just been rolled back, and saving through that would put the failed sale in the database - which is the
/// opposite of what a compensation is for. A fresh scope brings a fresh context and a fresh connection, so
/// the only thing pending is the one row this writes.
/// </remarks>
internal sealed partial class RefundLedger(
    IServiceScopeFactory scopeFactory,
    ILogger<RefundLedger> logger) : IRefundLedger
{
    public async Task<bool> RecordAsync(RefundOwedEvent refund, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            outbox.Enqueue(refund);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            RefundRecorded(logger, refund.Amount, refund.PaymentTransactionId, refund.Reason);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The last line of defence failed too, which nearly always means the database is the thing that
            // is wrong. Said out loud rather than thrown: the caller has its own error to report and a refund
            // it can still attempt by hand.
            RefundNotRecorded(logger, refund.Amount, refund.PaymentTransactionId, exception);

            return false;
        }
    }

    [LoggerMessage(
        EventId = 3400,
        Level = LogLevel.Warning,
        Message = "A refund of {Amount} against payment {PaymentTransactionId} was written to the outbox "
            + "({Reason}); the money goes back when the sweeper carries it")]
    private static partial void RefundRecorded(
        ILogger logger,
        decimal amount,
        string paymentTransactionId,
        string reason);

    [LoggerMessage(
        EventId = 3401,
        Level = LogLevel.Critical,
        Message = "A refund of {Amount} against payment {PaymentTransactionId} could not even be written "
            + "down; that money is held against a sale that never happened and now only this line knows")]
    private static partial void RefundNotRecorded(
        ILogger logger,
        decimal amount,
        string paymentTransactionId,
        Exception exception);
}
