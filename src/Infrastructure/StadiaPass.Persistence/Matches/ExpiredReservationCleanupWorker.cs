using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Persistence.Matches;

/// <summary>
/// Gives back the seats nobody finished buying.
/// </summary>
/// <remarks>
/// A hold lasts ten minutes, and until now the only thing that ever ended one was somebody else trying to
/// take the same seat - the aggregate releases an expired hold on the way past. That works for a popular
/// seat and not at all for the rest: an abandoned checkout leaves a seat that reads <c>Reserved</c> for
/// good, counted against the match and unsellable to anyone who does not happen to click it. The listing
/// then says one thing and the seat map another, and a match can run out of sellable seats without ever
/// reaching <see cref="MatchStatus.SoldOut"/>.
/// <para>
/// The domain still does the releasing, seat by seat, so the rules and the events stay where they belong.
/// Only the counters are handed to the database, for the same reason a sale hands them over: the totals this
/// sweep read are not the ones that should be written back.
/// </para>
/// </remarks>
internal sealed partial class ExpiredReservationCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpiredReservationCleanupWorker> logger) : BackgroundService
{
    /// <summary>
    /// A minute is fine. A hold is ten, so a seat is free again well inside the window a customer would
    /// notice, and the query is a cheap index read that finds nothing almost every time.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>Matches per pass, so a backlog is worked through steadily instead of in one long transaction.</summary>
    private const int MaxMatchesPerSweep = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Nothing is lost by a failed pass: the holds are still expired and the next minute finds
                // them again.
                SweepFailed(logger, exception);
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var matchRepository = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>().UtcNow;

        var matches = await matchRepository.GetWithExpiredReservationsAsync(
            now, MaxMatchesPerSweep, cancellationToken);

        foreach (var match in matches)
        {
            await ReleaseAsync(match, matchRepository, unitOfWork, now, cancellationToken);
        }
    }

    private async Task ReleaseAsync(
        Match match,
        IMatchRepository matchRepository,
        IUnitOfWork unitOfWork,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Read once. The aggregate is about to change its own copy of them, and the count of what was
        // released is what the counter update needs.
        var expired = match.Seats
            .Where(seat => seat.IsReservationExpired(now))
            .Select(seat => seat.SeatNumber.ToString())
            .ToArray();

        if (expired.Length is 0)
        {
            return;
        }

        // Memory only, and outside the transaction on purpose. The retrying execution strategy runs the
        // delegate below again after a transient failure, and these seats are no longer Reserved by then, so
        // a second pass would throw and lose the whole sweep to a blink of the network.
        foreach (var seatNumber in expired)
        {
            match.ReleaseSeat(seatNumber, now);
        }

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    // Coarsest row first, exactly as a sale does it, so the two never reach for the match
                    // and a seat in opposite orders.
                    await matchRepository.ApplySeatReleaseToCountersAsync(match, expired.Length, token);

                    await unitOfWork.SaveChangesAsync(token);
                },
                cancellationToken);

            SeatsReleased(logger, expired.Length, match.Id);
        }
        catch (ConcurrencyConflictException)
        {
            // Somebody bought or re-reserved one of these seats between the read and the write, which is a
            // perfectly good outcome: the seat is in use again and there is nothing left to release. The
            // whole match rolled back, and the next pass will pick up whatever is still expired.
            SeatsTakenFirst(logger, match.Id);
        }
    }

    [LoggerMessage(
        EventId = 2200,
        Level = LogLevel.Information,
        Message = "Released {ReleasedCount} expired seat holds on match {MatchId}")]
    private static partial void SeatsReleased(ILogger logger, int releasedCount, Guid matchId);

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Debug,
        Message = "Expired holds on match {MatchId} were claimed by somebody else before they could be "
            + "released; nothing to do")]
    private static partial void SeatsTakenFirst(ILogger logger, Guid matchId);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Error, Message = "An expired-hold sweep failed")]
    private static partial void SweepFailed(ILogger logger, Exception exception);
}
