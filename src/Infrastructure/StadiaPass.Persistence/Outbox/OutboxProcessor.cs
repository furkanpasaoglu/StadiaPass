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
    OutboxMetrics metrics,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often the sent rows are cleared out. There is no hurry about it; once a day is plenty.</summary>
    private static readonly TimeSpan PruneInterval = TimeSpan.FromDays(1);

    /// <summary>How long a delivered message is kept, in case somebody has to answer for it later.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>
    /// Small on purpose. The rows are locked for as long as the batch takes, and the batch takes as long as
    /// its slowest publish, so a big batch holds rows a second worker could have been getting on with.
    /// </summary>
    private const int BatchSize = 20;

    /// <summary>
    /// After this many refusals the message is set aside instead of being tried again every five seconds for
    /// as long as the process lives. Five is enough to ride out a broker restart, and few enough that a
    /// message which will never go stops writing a log line every tick and stops taking a slot in the batch.
    /// </summary>
    private const int MaxAttempts = 5;

    private DateTimeOffset? _lastPrunedUtc;

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

        await PublishBatchAsync(context, eventBus, dateTimeProvider, cancellationToken);
        await PruneAsync(context, dateTimeProvider, cancellationToken);
        await MeasureDepthAsync(context, cancellationToken);
    }

    private async Task PublishBatchAsync(
        StadiaPassDbContext context,
        IEventBus eventBus,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        // The Aspire Npgsql component configures a retrying execution strategy, and a retrying strategy
        // refuses to have a transaction opened behind its back - it would have no way to replay it. Handing
        // the whole batch to the strategy is how a transaction and retries are allowed to coexist, and it is
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
                         WHERE processed_on_utc IS NULL AND failed_on_utc IS NULL
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
            // Nothing about this one will get better by trying again, but it is counted like any other
            // refusal rather than special-cased: the ceiling stops it either way.
            GiveUpOrRetry(
                message,
                dateTimeProvider,
                $"No integration event is registered under the name {message.Type}.",
                exception: null);

            return;
        }

        try
        {
            await eventBus.PublishAsync(
                OutboxSerializer.Deserialize(message.Content, messageType), messageType, cancellationToken);

            message.ProcessedOnUtc = dateTimeProvider.UtcNow;
            message.Error = null;

            MessagePublished(logger, message.Id, message.Type, message.Attempts + 1);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The row stays exactly where it is. This is the whole point of the pattern.
            GiveUpOrRetry(message, dateTimeProvider, exception.Message, exception);
        }
    }

    /// <summary>
    /// Counts the refusal and, once there have been enough of them, sets the message aside. Left unprocessed
    /// either way rather than quietly ticked off: it was never delivered, and saying it was would be a lie
    /// told to whoever comes looking for the missing mail.
    /// </summary>
    private void GiveUpOrRetry(
        OutboxMessage message,
        IDateTimeProvider dateTimeProvider,
        string error,
        Exception? exception)
    {
        message.Attempts++;
        message.Error = error;

        if (message.Attempts >= MaxAttempts)
        {
            message.FailedOnUtc = dateTimeProvider.UtcNow;

            MessageAbandoned(logger, message.Id, message.Type, message.Attempts, exception);

            return;
        }

        PublishFailed(logger, message.Id, message.Type, message.Attempts, exception);
    }

    /// <summary>
    /// A delivered message has done its job, but the row outlives it. Months of them turn a table the sweeper
    /// reads every five seconds into mostly pages it will never look at, and every backup carries them.
    /// </summary>
    private async Task PruneAsync(
        StadiaPassDbContext context,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        if (_lastPrunedUtc is { } last && now - last < PruneInterval)
        {
            return;
        }

        var cutoff = now - Retention;

        // A statement of its own, outside the batch transaction: it is neither urgent nor worth holding locks
        // for, and a retrying strategy handles a single operation perfectly well on its own.
        var removed = await context.OutboxMessages
            .Where(message => message.ProcessedOnUtc != null && message.ProcessedOnUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        _lastPrunedUtc = now;

        if (removed is not 0)
        {
            MessagesPruned(logger, removed, Retention.Days);
        }
    }

    /// <summary>Counted here because the sweeper is at the table anyway; see <see cref="OutboxMetrics"/>.</summary>
    private async Task MeasureDepthAsync(StadiaPassDbContext context, CancellationToken cancellationToken)
    {
        var pending = await context.OutboxMessages
            .CountAsync(
                message => message.ProcessedOnUtc == null && message.FailedOnUtc == null,
                cancellationToken);

        var dead = await context.OutboxMessages
            .CountAsync(message => message.FailedOnUtc != null, cancellationToken);

        metrics.Record(pending, dead);
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Outbox message {OutboxMessageId} ({MessageType}) was published on attempt {Attempts}")]
    private static partial void MessagePublished(
        ILogger logger,
        Guid outboxMessageId,
        string messageType,
        int attempts);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "Outbox message {OutboxMessageId} ({MessageType}) could not be published on attempt "
            + "{Attempts}; it stays in the table and will be tried again")]
    private static partial void PublishFailed(
        ILogger logger,
        Guid outboxMessageId,
        string messageType,
        int attempts,
        Exception? exception);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Outbox message {OutboxMessageId} ({MessageType}) failed {Attempts} times and has been set "
            + "aside; it will not be tried again and needs a person to look at it")]
    private static partial void MessageAbandoned(
        ILogger logger,
        Guid outboxMessageId,
        string messageType,
        int attempts,
        Exception? exception);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Error, Message = "An outbox sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2104,
        Level = LogLevel.Information,
        Message = "Removed {RemovedCount} outbox messages delivered more than {RetentionDays} days ago")]
    private static partial void MessagesPruned(ILogger logger, int removedCount, int retentionDays);
}
