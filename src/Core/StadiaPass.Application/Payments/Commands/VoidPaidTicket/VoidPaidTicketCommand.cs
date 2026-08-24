using MediatR;

namespace StadiaPass.Application.Payments.Commands.VoidPaidTicket;

/// <summary>
/// Takes a ticket back because the money behind it went away - a chargeback, or a refund somebody issued
/// outside this application.
/// </summary>
/// <remarks>
/// Both of those arrive as provider events long after the request that made the sale, so this is deliberately
/// forgiving about finding nothing: a payment with no live ticket is the ordinary outcome when the refund was
/// this system's own compensation for a sale that never committed. Saying so quietly is the correct answer,
/// not an error.
/// </remarks>
public sealed record VoidPaidTicketCommand(string PaymentIntentId, string Reason) : IRequest;
