using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Categories.Commands.CreateCategory;

internal sealed class CreateCategoryCommandHandler(
    ISportCategoryRepository categoryRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        if (await categoryRepository.ExistsAsync(request.Name, cancellationToken: cancellationToken))
        {
            throw new ConflictException($"Category '{request.Name}' already exists.");
        }

        var category = SportCategory.Define(
            request.Name,
            request.Description,
            request.AllowedVenueKinds.Select(kind => Enum.Parse<VenueKind>(kind, ignoreCase: true)));

        await categoryRepository.AddAsync(category, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return category.ToDto();
    }
}
