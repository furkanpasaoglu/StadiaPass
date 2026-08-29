using FluentValidation;
using MediatR;

namespace StadiaPass.Application.Matches.Commands.CancelMatch;

/// <summary>
/// Calls a fixture off and starts giving the money back.
/// </summary>
/// <remarks>
/// Two halves on purpose. This one is small and synchronous: it shuts the till and hands back the seats
/// people are holding, so nothing further can be sold from the moment it returns. Paying back what was
/// already sold is the other half, and it happens ticket by ticket off the broker, because each ticket owes
/// somebody money and a refund the provider refuses has to be retried on its own rather than taking a whole
/// fixture's worth of them with it.
/// </remarks>
public sealed record CancelMatchCommand(Guid MatchId, string Reason) : IRequest;

internal sealed class CancelMatchCommandValidator : AbstractValidator<CancelMatchCommand>
{
    public CancelMatchCommandValidator()
    {
        RuleFor(command => command.MatchId).NotEmpty();

        // Travels with every refund to the provider and ends up on the customer's statement, so it is asked
        // for rather than defaulted, and kept short enough to survive the trip.
        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(200);
    }
}
