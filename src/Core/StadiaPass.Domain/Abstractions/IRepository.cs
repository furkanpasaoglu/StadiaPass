using StadiaPass.Domain.Common;

namespace StadiaPass.Domain.Abstractions;

public interface IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    Task<TAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    void Remove(TAggregate aggregate);
}
