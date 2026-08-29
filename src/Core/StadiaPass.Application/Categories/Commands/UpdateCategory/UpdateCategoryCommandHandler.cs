using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;
using StadiaPass.Domain.Venues;

namespace StadiaPass.Application.Categories.Commands.UpdateCategory;

internal sealed class UpdateCategoryCommandHandler(
    ISportCategoryRepository categoryRepository,
    IMatchRepository matchRepository,
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

        var venueKinds = request.AllowedVenueKinds
            .Select(kind => Enum.Parse<VenueKind>(kind, ignoreCase: true))
            .ToArray();

        var withdrawn = category.AllowedVenueKinds.Except(venueKinds).ToArray();

        if (withdrawn.Length is not 0
            && await matchRepository.ExistsForCategoryAsync(category.Id, cancellationToken))
        {
            throw new ConflictException(
                $"'{category.Name}' has matches open against it, so it can no longer stop being playable in "
                + $"{string.Join(", ", withdrawn)}. Deactivate it instead to stop new matches being opened.");
        }

        category.Rename(request.Name, request.Description);
        category.ChangeAllowedVenueKinds(venueKinds);

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
