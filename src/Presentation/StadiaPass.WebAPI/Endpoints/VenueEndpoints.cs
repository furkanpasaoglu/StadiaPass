using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Venues;
using StadiaPass.Application.Venues.Commands.DefineVenue;
using StadiaPass.Application.Venues.Queries.GetVenues;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

/// <summary>Back-office slice: defining the seating plans a match can be opened against.</summary>
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

        group.MapPost("/", DefineAsync)
            .WithName("DefineVenue")
            .WithSummary("Defines a venue and the blocks its seating plan is made of.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization(StadiaPassPermissions.Venues.Create);
    }

    private static async Task<Ok<IReadOnlyList<VenueDto>>> GetAllAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetVenuesQuery(), cancellationToken));

    private static async Task<Created<VenueDto>> DefineAsync(
        DefineVenueCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var venue = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/venues/{venue.Id}", venue);
    }
}
