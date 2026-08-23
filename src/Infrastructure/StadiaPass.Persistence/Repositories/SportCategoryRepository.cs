using Microsoft.EntityFrameworkCore;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;

namespace StadiaPass.Persistence.Repositories;

internal sealed class SportCategoryRepository(StadiaPassDbContext context)
    : Repository<SportCategory>(context), ISportCategoryRepository
{
    public async Task<IReadOnlyList<SportCategory>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .OrderBy(category => category.Name)
            .ToListAsync(cancellationToken);

    public Task<SportCategory?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Set.AsNoTracking().FirstOrDefaultAsync(category => category.Name == name, cancellationToken);

    public Task<bool> ExistsAsync(
        string name,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default) =>
        Set.AnyAsync(
            category => category.Name == name && (excludingId == null || category.Id != excludingId),
            cancellationToken);
}
