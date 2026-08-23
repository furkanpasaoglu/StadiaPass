using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.Application.Tickets;
using StadiaPass.Application.Tickets.Commands.ConfirmTicketSale;
using StadiaPass.Application.Tickets.Commands.CreateTicket;
using StadiaPass.Application.Tickets.Commands.ReserveTicket;
using StadiaPass.Application.Tickets.Queries.GetTicketById;
using StadiaPass.Application.Tickets.Queries.GetTicketsByMatch;
using StadiaPass.WebAPI.Contracts;

namespace StadiaPass.WebAPI.Endpoints;

internal sealed class TicketEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/tickets")
            .WithTags("Tickets");

        group.MapPost("/", CreateAsync)
            .WithName("CreateTicket")
            .WithSummary("Issues a new ticket for a match seat.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{ticketId:guid}", GetByIdAsync)
            .WithName("GetTicketById")
            .WithSummary("Returns a single ticket.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", GetByMatchAsync)
            .WithName("GetTicketsByMatch")
            .WithSummary("Returns every ticket issued for a match.");

        group.MapPost("/{ticketId:guid}/reservation", ReserveAsync)
            .WithName("ReserveTicket")
            .WithSummary("Reserves an available ticket for a holder.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{ticketId:guid}/sale", ConfirmSaleAsync)
            .WithName("ConfirmTicketSale")
            .WithSummary("Converts a reservation into a completed sale.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Created<TicketDto>> CreateAsync(
        CreateTicketCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var ticket = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/tickets/{ticket.Id}", ticket);
    }

    private static async Task<Ok<TicketDto>> GetByIdAsync(
        Guid ticketId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetTicketByIdQuery(ticketId), cancellationToken));

    private static async Task<Ok<IReadOnlyList<TicketDto>>> GetByMatchAsync(
        [FromQuery] Guid matchId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetTicketsByMatchQuery(matchId), cancellationToken));

    private static async Task<Ok<TicketDto>> ReserveAsync(
        Guid ticketId,
        ReserveTicketRequest request,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(
            new ReserveTicketCommand(ticketId, request.HolderReference),
            cancellationToken));

    private static async Task<Ok<TicketDto>> ConfirmSaleAsync(
        Guid ticketId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new ConfirmTicketSaleCommand(ticketId), cancellationToken));
}
