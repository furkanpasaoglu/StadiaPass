using MediatR;
using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common;

namespace StadiaPass.Persistence;

internal sealed class UnitOfWork(StadiaPassDbContext context, IPublisher publisher) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEvents = DrainDomainEvents();

        var affectedRows = await context.SaveChangesAsync(cancellationToken);

        foreach (var domainEvent in domainEvents)
        {
            await publisher.Publish(domainEvent, cancellationToken);
        }

        return affectedRows;
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(
            cancellationToken,
            async (token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);
                await operation(token);
                await transaction.CommitAsync(token);
            });

    private IDomainEvent[] DrainDomainEvents()
    {
        var aggregates = context.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count is not 0)
            .Select(entry => entry.Entity)
            .ToArray();

        var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToArray();

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        return domainEvents;
    }
}
