using FluentValidation;

namespace StadiaPass.Application.Tickets.Commands.CreateTicket;

internal sealed class CreateTicketCommandValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketCommandValidator()
    {
        RuleFor(command => command.MatchId)
            .NotEmpty().WithMessage("A match identifier is required.");

        RuleFor(command => command.Block)
            .NotEmpty().WithMessage("Seat block is required.")
            .MaximumLength(10).WithMessage("Seat block cannot exceed 10 characters.")
            .Matches("^[A-Za-z0-9-]+$").WithMessage("Seat block may only contain letters, digits and hyphens.");

        RuleFor(command => command.Row)
            .InclusiveBetween(1, 500).WithMessage("Seat row must be between 1 and 500.");

        RuleFor(command => command.Number)
            .InclusiveBetween(1, 500).WithMessage("Seat number must be between 1 and 500.");

        RuleFor(command => command.Price)
            .GreaterThan(0).WithMessage("Ticket price must be greater than zero.")
            .LessThanOrEqualTo(100_000).WithMessage("Ticket price exceeds the allowed maximum.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Ticket price supports at most 2 decimal places.");

        RuleFor(command => command.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-letter ISO 4217 code.");
    }
}
