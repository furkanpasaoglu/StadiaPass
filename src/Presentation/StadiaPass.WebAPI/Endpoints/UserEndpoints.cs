using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Identity.Users.Commands.CreateUser;
using StadiaPass.Application.Identity.Users.Commands.DeleteUser;
using StadiaPass.Application.Identity.Users.Commands.UpdateUser;
using StadiaPass.Application.Identity.Users.Commands.UpdateUserRoles;
using StadiaPass.Application.Identity.Users.Queries.GetUsers;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

internal sealed class UserEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization(StadiaPassPermissions.Users.Manage);

        group.MapGet("/", GetAllAsync)
            .WithName("GetUsers")
            .WithSummary("Returns a page of users with their roles, plus the assignable role names.");

        group.MapPost("/", CreateAsync)
            .WithName("CreateUser")
            .WithSummary("Creates a user in Keycloak and assigns the selected roles.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{userId}", UpdateAsync)
            .WithName("UpdateUser")
            .WithSummary("Updates the profile of a user.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{userId}/roles", UpdateRolesAsync)
            .WithName("UpdateUserRoles")
            .WithSummary("Replaces the roles assigned to a user.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{userId}", DeleteAsync)
            .WithName("DeleteUser")
            .WithSummary("Deletes a user.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<UserListDto>> GetAllAsync(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(
            new GetUsersQuery(search, page ?? 1, pageSize ?? 20),
            cancellationToken));

    private static async Task<Created<UserDto>> CreateAsync(
        CreateUserCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var user = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/users/{user.Id}", user);
    }

    private static async Task<Ok<UserDto>> UpdateAsync(
        string userId,
        UpdateUserRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(
            new UpdateUserCommand(userId, request.Email, request.FirstName, request.LastName, request.Enabled),
            cancellationToken));

    private static async Task<Ok<UserDto>> UpdateRolesAsync(
        string userId,
        UpdateUserRolesRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new UpdateUserRolesCommand(userId, request.Roles), cancellationToken));

    private static async Task<NoContent> DeleteAsync(
        string userId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteUserCommand(userId), cancellationToken);

        return TypedResults.NoContent();
    }
}

public sealed record UpdateUserRequest(string? Email, string? FirstName, string? LastName, bool Enabled);

public sealed record UpdateUserRolesRequest(IReadOnlyList<string> Roles);
