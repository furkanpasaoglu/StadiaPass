using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Messaging;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Persistence.Outbox;

namespace StadiaPass.Persistence.Inbox;

/// <summary>
/// Carries what a provider told us onto the broker, so the work it causes happens somewhere the provider is
/// not waiting. The outbox sweeper's twin, pointing the other way, and deliberately built the same: a batch
/// under <c>FOR UPDATE SKIP LOCKED</c>, an attempt ceiling, and rows that stay put until they are delivered.
/// </summary>
internal sealed partial class InboxProcessor(
    IServiceScopeFactory scopeFactory,
    InboxMetrics metrics,
    ILogger<InboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private const int BatchSize = 20;

    private const int MaxAttempts = 5;

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
        // refuses to have a transaction opened behind its back. Same reason, same shape, as everywhere else.
        await context.Database.CreateExecutionStrategy().ExecuteAsync(
            cancellationToken,
            async (token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);

                var pending = await context.InboxMessages
                    .FromSql(
                        $"""
                         SELECT * FROM stadiapass.inbox_messages
                         WHERE processed_on_utc IS NULL AND failed_on_utc IS NULL
                         ORDER BY received_on_utc
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

        await MeasureDepthAsync(context, cancellationToken);
    }

    /// <summary>Counted here because the sweeper is at the table anyway; see <see cref="InboxMetrics"/>.</summary>
    private async Task MeasureDepthAsync(StadiaPassDbContext context, CancellationToken cancellationToken)
    {
        var pending = await context.InboxMessages
            .CountAsync(
                message => message.ProcessedOnUtc == null && message.FailedOnUtc == null,
                cancellationToken);

        var dead = await context.InboxMessages
            .CountAsync(message => message.FailedOnUtc != null, cancellationToken);

        metrics.Record(pending, dead);
    }

    private async Task PublishAsync(
        InboxMessage message,
        IEventBus eventBus,
        IDateTimeProvider dateTimeProvider,
        CancellationToken cancellationToken)
    {
        if (!IntegrationEventTypes.TryResolve(message.Type, out var messageType))
        {
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
                OutboxSerializer.Deserialize(message.Payload, messageType), messageType, cancellationToken);

            message.ProcessedOnUtc = dateTimeProvider.UtcNow;
            message.Error = null;

            MessagePublished(logger, message.ProviderEventId, message.ProviderEventType);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GiveUpOrRetry(message, dateTimeProvider, exception.Message, exception);
        }
    }

    private void GiveUpOrRetry(
        InboxMessage message,
        IDateTimeProvider dateTimeProvider,
        string error,
        Exception? exception)
    {
        message.Attempts++;
        message.Error = error;

        if (message.Attempts >= MaxAttempts)
        {
            message.FailedOnUtc = dateTimeProvider.UtcNow;

            MessageAbandoned(logger, message.ProviderEventId, message.ProviderEventType, message.Attempts);

            return;
        }

        PublishFailed(logger, message.ProviderEventId, message.ProviderEventType, message.Attempts, exception);
    }

    [LoggerMessage(
        EventId = 2300,
        Level = LogLevel.Information,
        Message = "Inbox event {ProviderEventId} ({ProviderEventType}) was put on the bus")]
    private static partial void MessagePublished(
        ILogger logger,
        string providerEventId,
        string providerEventType);

    [LoggerMessage(
        EventId = 2301,
        Level = LogLevel.Warning,
        Message = "Inbox event {ProviderEventId} ({ProviderEventType}) could not be put on the bus on attempt "
            + "{Attempts}; it stays in the table and will be tried again")]
    private static partial void PublishFailed(
        ILogger logger,
        string providerEventId,
        string providerEventType,
        int attempts,
        Exception? exception);

    [LoggerMessage(
        EventId = 2302,
        Level = LogLevel.Error,
        Message = "Inbox event {ProviderEventId} ({ProviderEventType}) failed {Attempts} times and has been "
            + "set aside; the provider was told we had it, so this one needs a person")]
    private static partial void MessageAbandoned(
        ILogger logger,
        string providerEventId,
        string providerEventType,
        int attempts);

    [LoggerMessage(EventId = 2303, Level = LogLevel.Error, Message = "An inbox sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
