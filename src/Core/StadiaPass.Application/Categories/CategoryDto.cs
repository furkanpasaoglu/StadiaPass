using StadiaPass.Domain.Categories;

namespace StadiaPass.Application.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<string> AllowedVenueKinds);

internal static class CategoryMappings
{
    public static CategoryDto ToDto(this SportCategory category) => new(
        category.Id,
        category.Name,
        category.Description,
        category.IsActive,
        [.. category.AllowedVenueKinds.Select(kind => kind.ToString()).Order(StringComparer.Ordinal)]);
}
