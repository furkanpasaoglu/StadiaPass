namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// The way out to the broker. Only the outbox worker calls this: nothing that serves a request publishes
/// directly, because a request cannot promise both a database write and a network send.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// The type is passed alongside the message because the worker rebuilds it from a database row and holds
    /// it as <see cref="object"/>. A broker routes on the type, so it has to be the real one rather than
    /// whatever the compiler would infer at this call site.
    /// </summary>
    Task PublishAsync(object message, Type messageType, CancellationToken cancellationToken = default);
}
