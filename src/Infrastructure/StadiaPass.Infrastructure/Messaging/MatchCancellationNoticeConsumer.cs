using MassTransit;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Identity;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Tickets.Events;
using StadiaPass.Infrastructure.Email;

namespace StadiaPass.Infrastructure.Messaging;

/// <summary>
/// Tells a ticket holder their match was called off.
/// </summary>
/// <remarks>
/// <para>
/// A consumer of its own rather than a step inside the settlement, so that a mail server having a bad
/// afternoon retries by itself and cannot roll back a refund that was already recorded. The two are only
/// linked at the point they were written: the settlement stages this message in the same transaction that
/// cancels the ticket, so there is no state where somebody has lost a ticket and nothing is coming.
/// </para>
/// <para>
/// The address is looked up here because a ticket does not carry one - it knows the subject the identity
/// provider issued, and that is the only durable handle on a person this system has. Somebody who has since
/// deleted their account, or who never had an address on it, gets no mail; that is said out loud with the
/// ticket on it, because it is the one case where a person is owed money and nothing has told them.
/// </para>
/// </remarks>
internal sealed partial class MatchCancellationNoticeConsumer(
    IKeycloakAdminService keycloak,
    IEmailService emailService,
    ILogger<MatchCancellationNoticeConsumer> logger) : IConsumer<MatchCancellationNotice>
{
    public async Task Consume(ConsumeContext<MatchCancellationNotice> context)
    {
        var notice = context.Message;

        var holder = await keycloak.GetUserAsync(notice.HolderReference, context.CancellationToken);

        if (holder?.Email is not { Length: > 0 } recipient)
        {
            // Not thrown: retrying will not conjure an address, and parking the message in the error queue
            // would hide the refund behind an infrastructure problem it does not have. The refund itself is
            // on its own message and unaffected.
            NoAddress(logger, notice.TicketId, notice.HolderReference);

            return;
        }

        var sent = await emailService.SendEmailAsync(
            recipient,
            MatchCancellationEmail.SubjectFor(notice),
            MatchCancellationEmail.BodyFor(notice),
            context.CancellationToken);

        if (!sent)
        {
            // Thrown so the broker tries again. A confirmation that never arrives is an annoyance; a
            // cancellation that never arrives is somebody turning up at a stadium.
            throw new InvalidOperationException(
                $"The cancellation notice for ticket {notice.TicketId} could not be sent.");
        }

        NoticeSent(logger, notice.TicketId, notice.HomeTeam, notice.AwayTeam);
    }

    [LoggerMessage(
        EventId = 7400,
        Level = LogLevel.Information,
        Message = "Told the holder of ticket {TicketId} that {HomeTeam} v {AwayTeam} was called off")]
    private static partial void NoticeSent(ILogger logger, Guid ticketId, string homeTeam, string awayTeam);

    [LoggerMessage(
        EventId = 7401,
        Level = LogLevel.Warning,
        Message = "Ticket {TicketId} is being refunded but its holder {HolderReference} has no address on "
            + "their account, so nothing could be sent; they will only find out in the application")]
    private static partial void NoAddress(ILogger logger, Guid ticketId, string holderReference);
}
