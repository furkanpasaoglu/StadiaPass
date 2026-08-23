using MediatR;

namespace StadiaPass.Application.Categories.Commands.UpdateCategory;

public sealed record UpdateCategoryCommand(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<string> AllowedVenueKinds) : IRequest<CategoryDto>;
