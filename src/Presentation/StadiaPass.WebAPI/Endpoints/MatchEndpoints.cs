using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.Application.Matches;
using StadiaPass.Application.Matches.Commands.CancelMatch;
using StadiaPass.Application.Matches.Commands.CreateMatch;
using StadiaPass.Application.Matches.Commands.ReindexMatches;
using StadiaPass.Application.Matches.Queries.GetMatchRevenue;
using StadiaPass.Application.Matches.Queries.GetMatchSeatMap;
using StadiaPass.Application.Matches.Queries.GetUpcomingMatches;
using StadiaPass.Application.Matches.Queries.SearchMatches;
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

        // Public for the same reason browsing is: looking for a fixture is not something to sign in for.
        group.MapGet("/search", SearchAsync)
            .WithName("SearchMatches")
            .WithSummary("Finds upcoming matches by team, venue, city or sport, most relevant first.")
            .AllowAnonymous();

        // Guarded by the permission that opens a fixture rather than one of its own. Whoever puts matches on
        // sale is who rebuilds the index of what is on sale, and a permission has to be in the realm before
        // anybody can hold it - inventing one here would only buy a role nobody has.
        group.MapPost("/search/reindex", ReindexAsync)
            .WithName("ReindexMatchSearch")
            .WithSummary("Rebuilds the match search index from the database.")
            .RequireAuthorization(StadiaPassPermissions.Matches.Create);

        group.MapPost("/", CreateAsync)
            .WithName("CreateMatch")
            .WithSummary("Creates a match and materialises the whole venue seating plan as available seats.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(StadiaPassPermissions.Matches.Create);

        // Its own permission rather than the one that opens a fixture. Cancelling spends money - every
        // ticket sold is refunded - and whoever may put a match on sale is not automatically whoever may
        // hand back a stadium's worth of takings.
        group.MapPost("/{matchId:guid}/cancellation", CancelAsync)
            .WithName("CancelMatch")
            .WithSummary("Calls a match off: selling stops at once and every ticket sold is refunded.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(StadiaPassPermissions.Matches.Cancel);

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

        group.MapGet("/{matchId:guid}/revenue", GetRevenueAsync)
            .WithName("GetMatchRevenue")
            .WithSummary("Returns what a match has taken: tickets sold, refunds, net revenue and occupancy.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(StadiaPassPermissions.Analytics.ViewRevenue);
    }

    private static async Task<Ok<IReadOnlyList<MatchDto>>> GetUpcomingAsync(
        [FromQuery] string? category,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetUpcomingMatchesQuery(category), cancellationToken));

    private static async Task<Ok<MatchRevenueDto>> GetRevenueAsync(
        Guid matchId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMatchRevenueQuery(matchId), cancellationToken));

    private static async Task<Ok<MatchSearchResultDto>> SearchAsync(
        [FromQuery] string? q,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new SearchMatchesQuery(q ?? string.Empty), cancellationToken));

    private static async Task<Ok<ReindexMatchesResultDto>> ReindexAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ReindexMatchesCommand(), cancellationToken));

    private static async Task<NoContent> CancelAsync(
        Guid matchId,
        CancelMatchRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(new CancelMatchCommand(matchId, request.Reason), cancellationToken);

        return TypedResults.NoContent();
    }

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

/// <summary>
/// Why the fixture is being called off. Taken in a body rather than a query string because it travels with
/// every refund this sets off and ends up on the customer's statement.
/// </summary>
internal sealed record CancelMatchRequest(string Reason);
