using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Venues;
using StadiaPass.Application.Venues.Commands.CreateVenue;
using StadiaPass.Application.Venues.Commands.DeleteVenue;
using StadiaPass.Application.Venues.Commands.UpdateVenue;
using StadiaPass.Application.Venues.Queries.GetVenues;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

/// <summary>Back-office slice: the seating plans a match can be opened against.</summary>
internal sealed class VenueEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/venues")
            .WithTags("Venues");

        group.MapGet("/", GetAllAsync)
            .WithName("GetVenues")
            .WithSummary("Returns every venue with its seating plan.")
            .RequireAuthorization(StadiaPassPermissions.Venues.View);

        group.MapPost("/", CreateAsync)
            .WithName("CreateVenue")
            .WithSummary("Defines a venue and the blocks its seating plan is made of.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Venues.Create);

        group.MapPut("/{venueId:guid}", UpdateAsync)
            .WithName("UpdateVenue")
            .WithSummary("Updates a venue; the seating plan may only be reshaped while no match uses it.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Venues.Update);

        group.MapDelete("/{venueId:guid}", DeleteAsync)
            .WithName("DeleteVenue")
            .WithSummary("Deletes a venue that no match uses.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Venues.Delete);
    }

    private static async Task<Ok<IReadOnlyList<VenueDto>>> GetAllAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetVenuesQuery(), cancellationToken));

    private static async Task<Created<VenueDto>> CreateAsync(
        CreateVenueCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var venue = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/venues/{venue.Id}", venue);
    }

    private static async Task<Ok<VenueDto>> UpdateAsync(
        Guid venueId,
        UpdateVenueRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(
            new UpdateVenueCommand(venueId, request.Name, request.City, request.Kind, request.Blocks),
            cancellationToken));

    private static async Task<NoContent> DeleteAsync(
        Guid venueId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteVenueCommand(venueId), cancellationToken);

        return TypedResults.NoContent();
    }
}

public sealed record UpdateVenueRequest(
    string Name,
    string City,
    string Kind,
    IReadOnlyList<VenueBlockInput>? Blocks);
