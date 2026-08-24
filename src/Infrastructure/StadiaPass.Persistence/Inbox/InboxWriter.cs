using Microsoft.EntityFrameworkCore;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Messaging;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Persistence.Outbox;

namespace StadiaPass.Persistence.Inbox;

/// <summary>
/// Writes an incoming event down and saves it there and then - unlike the outbox writer, which joins whatever
/// transaction its caller already has. There is no caller transaction here: a webhook endpoint has one job,
/// which is to get the event onto disk before answering the provider.
/// </summary>
internal sealed class InboxWriter(StadiaPassDbContext context, IDateTimeProvider dateTimeProvider) : IInbox
{
    public async Task<bool> TryRecordAsync(
        string providerEventId,
        string providerEventType,
        object message,
        CancellationToken cancellationToken = default)
    {
        if (!IntegrationEventTypes.IsKnown(message))
        {
            throw new InvalidOperationException(
                $"'{IntegrationEventTypes.NameOf(message)}' is not a registered integration event. "
                + $"Add it to {nameof(IntegrationEventTypes)} before putting it on the inbox.");
        }

        context.InboxMessages.Add(new InboxMessage
        {
            Id = Guid.CreateVersion7(),
            ProviderEventId = providerEventId,
            ProviderEventType = providerEventType,
            Type = IntegrationEventTypes.NameOf(message),
            Payload = OutboxSerializer.Serialize(message),
            ReceivedOnUtc = dateTimeProvider.UtcNow
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();

            // Asked of the database rather than read out of the exception. Matching on a constraint name
            // would tie this to one provider's error text; asking whether the row is there answers the
            // actual question and stays right whatever the driver decided to call the violation. A failure
            // that is not a redelivery is still a failure and goes on up.
            if (await AlreadyRecordedAsync(providerEventId, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    private Task<bool> AlreadyRecordedAsync(string providerEventId, CancellationToken cancellationToken) =>
        context.InboxMessages
            .AsNoTracking()
            .AnyAsync(message => message.ProviderEventId == providerEventId, cancellationToken);
}
