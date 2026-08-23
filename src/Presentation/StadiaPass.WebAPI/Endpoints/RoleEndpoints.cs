using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Identity.Roles.Commands.CreateRoleWithPermissions;
using StadiaPass.Application.Identity.Roles.Commands.DeleteRole;
using StadiaPass.Application.Identity.Roles.Commands.UpdateRolePermissions;
using StadiaPass.Application.Identity.Roles.Queries.GetRoles;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

/// <summary>
/// Identity portal: roles and the permissions bound to them. Nothing is stored locally - every call is
/// brokered to the Keycloak Admin REST API.
/// </summary>
internal sealed class RoleEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/roles")
            .WithTags("Roles")
            .RequireAuthorization(StadiaPassPermissions.Roles.Manage);

        group.MapGet("/", GetAllAsync)
            .WithName("GetRoles")
            .WithSummary("Returns business roles with their permissions, plus the permission catalogue.");

        group.MapPost("/", CreateAsync)
            .WithName("CreateRoleWithPermissions")
            .WithSummary("Creates a role and binds the selected permissions to it.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{roleName}/permissions", UpdatePermissionsAsync)
            .WithName("UpdateRolePermissions")
            .WithSummary("Replaces the permissions bound to a role.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{roleName}", DeleteAsync)
            .WithName("DeleteRole")
            .WithSummary("Deletes a business role.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<RoleListDto>> GetAllAsync(ISender sender, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetRolesQuery(), cancellationToken));

    private static async Task<Created<RoleDto>> CreateAsync(
        CreateRoleWithPermissionsCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var role = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/roles/{role.Name}", role);
    }

    private static async Task<Ok<RoleDto>> UpdatePermissionsAsync(
        string roleName,
        UpdateRolePermissionsRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(
            new UpdateRolePermissionsCommand(roleName, request.Permissions), cancellationToken));

    private static async Task<NoContent> DeleteAsync(
        string roleName,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteRoleCommand(roleName), cancellationToken);

        return TypedResults.NoContent();
    }
}

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);
