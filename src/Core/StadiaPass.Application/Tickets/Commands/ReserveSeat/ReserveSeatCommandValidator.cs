using FluentValidation;

namespace StadiaPass.Application.Tickets.Commands.ReserveSeat;

internal sealed class ReserveSeatCommandValidator : AbstractValidator<ReserveSeatCommand>
{
    public ReserveSeatCommandValidator()
    {
        RuleFor(command => command.MatchId)
            .NotEmpty().WithMessage("A match identifier is required.");

        RuleFor(command => command.SeatNumber)
            .NotEmpty().WithMessage("A seat number is required.")
            .Matches("^[A-Za-z0-9-]+-[0-9]+-[0-9]+$")
            .WithMessage("Seat number must look like BLOCK-ROW-NUMBER, for example A1-12-7.");
    }
}
