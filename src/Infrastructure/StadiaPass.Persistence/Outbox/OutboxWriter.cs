using System.Text.Json;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Messaging;
using StadiaPass.Application.Infrastructure.Abstractions;

namespace StadiaPass.Persistence.Outbox;

/// <summary>
/// How a message becomes a row. Both ends of the outbox live in this project on purpose: the JSON is a
/// storage format, and nothing outside here should have an opinion about it.
/// </summary>
internal static class OutboxSerializer
{
    /// <summary>
    /// Written and read by the same options, deliberately spelled out rather than defaulted. These rows can
    /// outlive a deployment, so the shape they are read back with has to be the shape they were written in.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    public static string Serialize(object message) =>
        JsonSerializer.Serialize(message, message.GetType(), Options);

    public static object Deserialize(string content, Type messageType) =>
        JsonSerializer.Deserialize(content, messageType, Options)
        ?? throw new InvalidOperationException($"An outbox row of type '{messageType.Name}' read back as null.");
}

internal sealed class OutboxWriter(StadiaPassDbContext context, IDateTimeProvider dateTimeProvider) : IOutbox
{
    public void Enqueue(object message)
    {
        // Refused here rather than at the far end. An unregistered type would be written happily and then sit
        // in the table failing to publish every five seconds, which is a slow and confusing way to find out.
        if (!IntegrationEventTypes.IsKnown(message))
        {
            throw new InvalidOperationException(
                $"'{IntegrationEventTypes.NameOf(message)}' is not a registered integration event. "
                + $"Add it to {nameof(IntegrationEventTypes)} before putting it on the outbox.");
        }

        // Added, not saved. It goes to the database with the caller's own transaction.
        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            OccurredOnUtc = dateTimeProvider.UtcNow,
            Type = IntegrationEventTypes.NameOf(message),
            Content = OutboxSerializer.Serialize(message)
        });
    }
}
