using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Categories.Commands.UpdateCategory;

internal sealed class UpdateCategoryCommandHandler(
    ISportCategoryRepository categoryRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    public async Task<CategoryDto> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categoryRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(SportCategory), request.Id);

        if (await categoryRepository.ExistsAsync(request.Name, request.Id, cancellationToken))
        {
            throw new ConflictException($"Category '{request.Name}' already exists.");
        }

        category.Rename(request.Name, request.Description);
        category.ChangeAllowedVenueKinds(
            request.AllowedVenueKinds.Select(kind => Enum.Parse<VenueKind>(kind, ignoreCase: true)));

        if (request.IsActive)
        {
            category.Activate();
        }
        else
        {
            category.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return category.ToDto();
    }
}
