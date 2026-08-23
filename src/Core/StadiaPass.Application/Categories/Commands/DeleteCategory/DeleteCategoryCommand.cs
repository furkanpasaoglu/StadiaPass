using MediatR;

namespace StadiaPass.Application.Categories.Commands.DeleteCategory;

public sealed record DeleteCategoryCommand(Guid Id) : IRequest;
