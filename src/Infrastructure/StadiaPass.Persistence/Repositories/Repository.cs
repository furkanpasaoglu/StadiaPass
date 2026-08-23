using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common;

namespace StadiaPass.Persistence.Repositories;

internal abstract class Repository<TAggregate>(StadiaPassDbContext context) : IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    protected StadiaPassDbContext Context { get; } = context;

    protected DbSet<TAggregate> Set => Context.Set<TAggregate>();

    public virtual async Task<TAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(aggregate => aggregate.Id == id, cancellationToken);

    public virtual async Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default) =>
        await Set.AddAsync(aggregate, cancellationToken);

    public virtual void Remove(TAggregate aggregate) => Set.Remove(aggregate);
}
