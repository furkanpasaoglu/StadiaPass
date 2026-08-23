using FluentValidation;

namespace StadiaPass.Application.Tickets.Commands.ReserveTicket;

internal sealed class ReserveTicketCommandValidator : AbstractValidator<ReserveTicketCommand>
{
    public ReserveTicketCommandValidator()
    {
        RuleFor(command => command.TicketId)
            .NotEmpty().WithMessage("A ticket identifier is required.");

        RuleFor(command => command.HolderReference)
            .NotEmpty().WithMessage("A holder reference is required.")
            .MaximumLength(64).WithMessage("Holder reference cannot exceed 64 characters.");
    }
}
