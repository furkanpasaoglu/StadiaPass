using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Matches.Events;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.Matches.Commands.CancelMatch;

internal sealed partial class CancelMatchCommandHandler(
    IMatchRepository matchRepository,
    ICacheService cacheService,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<CancelMatchCommandHandler> logger) : IRequestHandler<CancelMatchCommand>
{
    public async Task Handle(CancelMatchCommand request, CancellationToken cancellationToken)
    {
        var match = await matchRepository.GetWithHeldSeatsAsync(request.MatchId, cancellationToken)
            ?? throw new NotFoundException(nameof(Match), request.MatchId);

        var now = dateTimeProvider.UtcNow;

        // Counted before the aggregate is asked to do anything, because afterwards the answer is always zero
        // and the counter update has to be told how far to move.
        var heldSeatCount = match.Seats.Count(seat => seat.Status is SeatStatus.Reserved);

        // In memory and outside the transaction, for the reason the sweeper spells out: the retrying
        // execution strategy runs the delegate below again after a transient failure, and by then these seats
        // are no longer held and the fixture is already cancelled, so a second pass would throw and lose a
        // cancellation to a blink of the network.
        match.Cancel(now);

        // Staged before the transaction and written by the save inside it, so the announcement and the
        // cancellation share one fate. Publishing after the commit would leave a gap where a fixture is
        // called off and nobody downstream is ever told - which here means tickets nobody refunds.
        outbox.Enqueue(new MatchCancelledEvent(match.Id, request.Reason, now));

        // The match row is the coarsest lock in the system, so it is taken last, immediately before the
        // commit, exactly as every other write path takes it.
        var writeCounters = matchRepository.PrepareMatchCancellationCounters(match, heldSeatCount);

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await unitOfWork.SaveChangesAsync(token);

                await writeCounters(token);
            },
            cancellationToken);

        // The listing is cached and filters cancelled fixtures out, so until this runs the front page keeps
        // offering a match that no longer exists.
        await cacheService.RemoveAsync(MatchCacheKeys.Upcoming, cancellationToken);

        MatchCancelled(logger, match.Id, heldSeatCount, request.Reason);
    }

    [LoggerMessage(
        EventId = 7200,
        Level = LogLevel.Warning,
        Message = "Match {MatchId} was cancelled ({Reason}); {HeldSeatCount} held seats were given back and "
            + "every sold seat is now owed a refund")]
    private static partial void MatchCancelled(
        ILogger logger,
        Guid matchId,
        int heldSeatCount,
        string reason);
}
