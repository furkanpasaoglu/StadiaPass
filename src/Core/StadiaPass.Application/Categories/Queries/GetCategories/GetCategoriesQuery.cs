using MediatR;

namespace StadiaPass.Application.Categories.Queries.GetCategories;

public sealed record GetCategoriesQuery(bool ActiveOnly = false) : IRequest<IReadOnlyList<CategoryDto>>;
