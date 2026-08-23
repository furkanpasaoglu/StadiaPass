using StadiaPass.Domain.Categories;

namespace StadiaPass.Domain.Abstractions;

public interface ISportCategoryRepository : IRepository<SportCategory>
{
    Task<IReadOnlyList<SportCategory>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SportCategory?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string name, Guid? excludingId = null, CancellationToken cancellationToken = default);
}
