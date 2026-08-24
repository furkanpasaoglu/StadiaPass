using MassTransit;
using StadiaPass.Application.Infrastructure.Abstractions;

namespace StadiaPass.Infrastructure.Messaging;

/// <summary>
/// The one place that knows the broker is MassTransit over RabbitMQ. Everything upstream of here writes to
/// the outbox and knows nothing about transports.
/// </summary>
internal sealed class MassTransitEventBus(IPublishEndpoint publishEndpoint) : IEventBus
{
    public Task PublishAsync(object message, Type messageType, CancellationToken cancellationToken = default) =>
        publishEndpoint.Publish(message, messageType, cancellationToken);
}
