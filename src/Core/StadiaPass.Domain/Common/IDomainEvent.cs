using MediatR;

namespace StadiaPass.Domain.Common;

public interface IDomainEvent : INotification
{
    Guid EventId { get; }

    DateTimeOffset OccurredOnUtc { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}
