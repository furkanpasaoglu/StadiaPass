using MediatR;
using Microsoft.EntityFrameworkCore;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common;

namespace StadiaPass.Persistence;

internal sealed class UnitOfWork(StadiaPassDbContext context, IPublisher publisher) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        int affectedRows;

        try
        {
            affectedRows = await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // A concurrency token did not match, so the whole SaveChanges was rolled back: another
            // transaction wrote one of these rows first. The translation happens here because
            // DbUpdateConcurrencyException is an EF Core type and the application layer does not - and
            // should not - reference EF Core. Handlers catch the exception below instead.
            throw new ConcurrencyConflictException(
                BuildConflictMessage(exception), exception);
        }

        // Drained only once the write has committed. Draining first reads well - the events are gathered from
        // the very entities about to be written - but it empties the aggregates whether the save works or
        // not, and the retrying execution strategy runs a failed transaction again. That second attempt would
        // find nothing left to announce, so a sale would commit with no SeatSold event behind it.
        var domainEvents = DrainDomainEvents();

        // Only a committed write is worth announcing: a failed save throws above and nothing is drained.
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

    /// <summary>Names the entity that lost the race, so the log says more than "something conflicted".</summary>
    private static string BuildConflictMessage(DbUpdateConcurrencyException exception)
    {
        var entityName = exception.Entries.Count is not 0
            ? exception.Entries[0].Metadata.ClrType.Name
            : "record";

        return $"The {entityName} was changed by another transaction after this request read it, so the write was refused.";
    }

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
