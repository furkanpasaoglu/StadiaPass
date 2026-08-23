using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.Application.Categories;
using StadiaPass.Application.Categories.Commands.CreateCategory;
using StadiaPass.Application.Categories.Commands.DeleteCategory;
using StadiaPass.Application.Categories.Commands.UpdateCategory;
using StadiaPass.Application.Categories.Queries.GetCategories;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

/// <summary>Catalogue of sports a match can be opened for. Each verb carries its own permission.</summary>
internal sealed class CategoryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/categories")
            .WithTags("Categories");

        group.MapGet("/", GetAllAsync)
            .WithName("GetCategories")
            .WithSummary("Returns sport categories and the venue kinds each can be played in.")
            .RequireAuthorization(StadiaPassPermissions.Categories.View);

        group.MapPost("/", CreateAsync)
            .WithName("CreateCategory")
            .WithSummary("Adds a sport category.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Categories.Create);

        group.MapPut("/{categoryId:guid}", UpdateAsync)
            .WithName("UpdateCategory")
            .WithSummary("Renames a category or changes where it can be played.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Categories.Update);

        group.MapDelete("/{categoryId:guid}", DeleteAsync)
            .WithName("DeleteCategory")
            .WithSummary("Deletes a category that no match uses.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Categories.Delete);
    }

    private static async Task<Ok<IReadOnlyList<CategoryDto>>> GetAllAsync(
        [FromQuery] bool? activeOnly,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetCategoriesQuery(activeOnly ?? false), cancellationToken));

    private static async Task<Created<CategoryDto>> CreateAsync(
        CreateCategoryCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var category = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/categories/{category.Id}", category);
    }

    private static async Task<Ok<CategoryDto>> UpdateAsync(
        Guid categoryId,
        UpdateCategoryRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(
            new UpdateCategoryCommand(
                categoryId, request.Name, request.Description, request.IsActive, request.AllowedVenueKinds),
            cancellationToken));

    private static async Task<NoContent> DeleteAsync(
        Guid categoryId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteCategoryCommand(categoryId), cancellationToken);

        return TypedResults.NoContent();
    }
}

public sealed record UpdateCategoryRequest(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<string> AllowedVenueKinds);
