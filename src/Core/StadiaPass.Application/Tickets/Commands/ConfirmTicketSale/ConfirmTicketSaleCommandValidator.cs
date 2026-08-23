using FluentValidation;

namespace StadiaPass.Application.Tickets.Commands.ConfirmTicketSale;

internal sealed class ConfirmTicketSaleCommandValidator : AbstractValidator<ConfirmTicketSaleCommand>
{
    public ConfirmTicketSaleCommandValidator() =>
        RuleFor(command => command.TicketId)
            .NotEmpty().WithMessage("A ticket identifier is required.");
}
