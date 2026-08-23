using MediatR;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Categories;

namespace StadiaPass.Application.Categories.Commands.DeleteCategory;

internal sealed class DeleteCategoryCommandHandler(
    ISportCategoryRepository categoryRepository,
    IMatchRepository matchRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<DeleteCategoryCommand>
{
    public async Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await categoryRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(SportCategory), request.Id);

        if (await matchRepository.ExistsForCategoryAsync(category.Id, cancellationToken))
        {
            throw new ConflictException(
                $"'{category.Name}' is used by at least one match. Deactivate it instead of deleting it.");
        }

        categoryRepository.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
