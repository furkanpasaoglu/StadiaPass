using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.Application.Matches;
using StadiaPass.Application.Matches.Commands.CreateMatch;
using StadiaPass.Application.Matches.Queries.GetMatchSeatMap;
using StadiaPass.Application.Matches.Queries.GetUpcomingMatches;
using StadiaPass.Application.Tickets;
using StadiaPass.Application.Tickets.Commands.ReserveSeat;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

internal sealed class MatchEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/matches")
            .WithTags("Matches");

        // Browsing is public, the way a ticketing site works: a visitor sees what is on and which seats
        // are taken, and is only asked to sign in when they try to hold one.
        group.MapGet("/", GetUpcomingAsync)
            .WithName("GetUpcomingMatches")
            .WithSummary("Returns upcoming matches, optionally filtered by sport category.")
            .AllowAnonymous();

        group.MapPost("/", CreateAsync)
            .WithName("CreateMatch")
            .WithSummary("Creates a match and materialises the whole venue seating plan as available seats.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(StadiaPassPermissions.Matches.Create);

        group.MapGet("/{matchId:guid}/seats", GetSeatMapAsync)
            .WithName("GetMatchSeatMap")
            .WithSummary("Returns the seat map of a match grouped by block and row, with each seat status.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        group.MapPost("/{matchId:guid}/seats/{seatNumber}/reservation", ReserveSeatAsync)
            .WithName("ReserveSeat")
            .WithSummary("Holds a seat for the signed-in customer until the reservation window expires.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(StadiaPassPermissions.Tickets.Reserve);
    }

    private static async Task<Ok<IReadOnlyList<MatchDto>>> GetUpcomingAsync(
        [FromQuery] string? category,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetUpcomingMatchesQuery(category), cancellationToken));

    private static async Task<Created<MatchDto>> CreateAsync(
        CreateMatchCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var match = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/matches/{match.Id}", match);
    }

    private static async Task<Ok<SeatMapDto>> GetSeatMapAsync(
        Guid matchId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMatchSeatMapQuery(matchId), cancellationToken));

    private static async Task<Ok<SeatReservationDto>> ReserveSeatAsync(
        Guid matchId,
        string seatNumber,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ReserveSeatCommand(matchId, seatNumber), cancellationToken));
}
