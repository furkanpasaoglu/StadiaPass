using MediatR;
using StadiaPass.Domain.Abstractions;

namespace StadiaPass.Application.Categories.Queries.GetCategories;

internal sealed class GetCategoriesQueryHandler(ISportCategoryRepository categoryRepository)
    : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<IReadOnlyList<CategoryDto>> Handle(
        GetCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        var categories = await categoryRepository.GetAllAsync(cancellationToken);

        return
        [
            .. categories
                .Where(category => !request.ActiveOnly || category.IsActive)
                .Select(category => category.ToDto())
        ];
    }
}
