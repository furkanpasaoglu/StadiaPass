using MediatR;

namespace StadiaPass.Application.Categories.Commands.CreateCategory;

public sealed record CreateCategoryCommand(
    string Name,
    string? Description,
    IReadOnlyList<string> AllowedVenueKinds) : IRequest<CategoryDto>;
