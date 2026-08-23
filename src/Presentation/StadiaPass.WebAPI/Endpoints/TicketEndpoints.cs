using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using StadiaPass.Application.Tickets;
using StadiaPass.Application.Tickets.Commands.ConfirmTicketPurchase;
using StadiaPass.Application.Tickets.Queries.GetMyTickets;
using StadiaPass.Application.Tickets.Queries.GetTicketById;
using StadiaPass.SharedKernel.Authorization;

namespace StadiaPass.WebAPI.Endpoints;

internal sealed class TicketEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup("/api/v1/tickets")
            .WithTags("Tickets");

        group.MapPost("/", PurchaseAsync)
            .WithName("ConfirmTicketPurchase")
            .WithSummary("Turns the caller's own seat reservation into a sale and issues the ticket.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(StadiaPassPermissions.Tickets.Purchase);

        group.MapGet("/mine", GetMineAsync)
            .WithName("GetMyTickets")
            .WithSummary("Returns the tickets issued to the signed-in customer.")
            .RequireAuthorization(StadiaPassPermissions.Tickets.View);

        group.MapGet("/{ticketId:guid}", GetByIdAsync)
            .WithName("GetTicketById")
            .WithSummary("Returns a single ticket.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(StadiaPassPermissions.Tickets.View);
    }

    private static async Task<Created<TicketDto>> PurchaseAsync(
        ConfirmTicketPurchaseCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var ticket = await sender.Send(command, cancellationToken);

        return TypedResults.Created($"/api/v1/tickets/{ticket.Id}", ticket);
    }

    private static async Task<Ok<IReadOnlyList<TicketDto>>> GetMineAsync(
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetMyTicketsQuery(), cancellationToken));

    private static async Task<Ok<TicketDto>> GetByIdAsync(
        Guid ticketId,
        ISender sender,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await sender.Send(new GetTicketByIdQuery(ticketId), cancellationToken));
}
