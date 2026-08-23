using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Common.Authorization;
using StadiaPass.Application.Matches;
using StadiaPass.Application.Matches.Commands.ScheduleMatch;
using StadiaPass.Application.Matches.Queries.GetUpcomingMatches;

namespace StadiaPass.WebAPI.Endpoints;

internal sealed class MatchEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/matches")
            .WithTags("Matches");

        group.MapGet("/", GetUpcomingAsync)
            .WithName("GetUpcomingMatches")
            .WithSummary("Returns matches that have not kicked off yet.")
            .RequireAuthorization(StadiaPassPermissions.Matches.View);

        group.MapPost("/", ScheduleAsync)
            .WithName("ScheduleMatch")
            .WithSummary("Schedules a new match and opens ticket sales.")
            .ProducesValidationProblem()
            .RequireAuthorization(StadiaPassPermissions.Matches.Create);
    }

    private static async Task<Ok<IReadOnlyList<MatchDto>>> GetUpcomingAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetUpcomingMatchesQuery(), cancellationToken));

    private static async Task<Created<MatchDto>> ScheduleAsync(
        ScheduleMatchCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var match = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/matches/{match.Id}", match);
    }
}
