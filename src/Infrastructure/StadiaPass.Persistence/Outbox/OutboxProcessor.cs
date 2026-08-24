using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Messaging;
using StadiaPass.Application.Infrastructure.Abstractions;

namespace StadiaPass.Persistence.Outbox;

/// <summary>
/// Carries what the outbox holds to the broker, and marks off what got there. A broker that is down is
/// therefore a delay rather than a lost ticket: the rows wait, and the next sweep finds them.
/// </summary>
/// <remarks>
/// Delivery is at least once, never exactly once. A message can reach the broker and the row that records it
/// can still fail to commit, and the only honest answer to that is to send it again - so consumers have to be
/// able to see the same message twice without doing the work twice.
/// </remarks>
internal sealed partial class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Small on purpose. The rows are locked for as long as the batch takes, and the batch takes as long as
    /// its slowest publish, so a big batch holds rows a second worker could have been getting on with.
    /// </summary>
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // This loop has to outlive anything one sweep can run into. Nothing was lost - the messages
                // are still in the table - so the answer is to say so and come back in five seconds.
                SweepFailed(logger, exception);
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<StadiaPassDbContext>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // The Aspire Npgsql component configures a retrying execution strategy, and a retrying strategy
        // refuses to have a transaction opened behind its back - it would have no way to replay it. Handing
        // the whole sweep to the strategy is how a transaction and retries are allowed to coexist, and it is
        // the same shape the unit of work uses for exactly the same reason.
        await context.Database.CreateExecutionStrategy().ExecuteAsync(
            cancellationToken,
            async (token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);

                // FOR UPDATE SKIP LOCKED is what lets a second instance of the API run this worker too: it
                // takes the next batch rather than waiting for this one, and never the same rows. Without it
                // two instances would publish every message twice.
                var pending = await context.OutboxMessages
                    .FromSql(
                        $"""
                         SELECT * FROM stadiapass.outbox_messages
                         WHERE processed_on_utc IS NULL
                         ORDER BY occurred_on_utc
                         LIMIT {BatchSize}
                         FOR UPDATE SKIP LOCKED
                         """)
                    .ToListAsync(token);

                if (pending.Count is 0)
                {
                    return;
                }

                foreach (var message in pending)
                {
                    await PublishAsync(message, eventBus, dateTimeProvider, token);
                }

                await context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            });
    }

    private async Task PublishAsync(
        OutboxMessage message,
        IEventBus eventBus,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        if (!IntegrationEventTypes.TryResolve(message.Type, out var messageType))
        {
            // Left unprocessed rather than quietly ticked off: it was never delivered, and saying it was
            // would be a lie told to whoever comes looking for the missing mail.
            message.Error = $"'{message.Type}' is not a registered integration event.";
            UnknownMessageType(logger, message.Id, message.Type);

            return;
        }

        try
        {
            await eventBus.PublishAsync(
                OutboxSerializer.Deserialize(message.Content, messageType), messageType, cancellationToken);

            message.ProcessedOnUtc = dateTimeProvider.UtcNow;
            message.Error = null;

            MessagePublished(logger, message.Id, message.Type);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The row stays exactly where it is. This is the whole point of the pattern.
            message.Error = exception.Message;

            PublishFailed(logger, message.Id, message.Type, exception);
        }
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Outbox message {OutboxMessageId} ({MessageType}) was published")]
    private static partial void MessagePublished(ILogger logger, Guid outboxMessageId, string messageType);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "Outbox message {OutboxMessageId} ({MessageType}) could not be published; it stays in the "
            + "table and will be tried again")]
    private static partial void PublishFailed(
        ILogger logger,
        Guid outboxMessageId,
        string messageType,
        Exception exception);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Outbox message {OutboxMessageId} names {MessageType}, which nothing knows how to read; it "
            + "will never be delivered without either the type back or the row gone")]
    private static partial void UnknownMessageType(ILogger logger, Guid outboxMessageId, string messageType);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Error, Message = "An outbox sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
