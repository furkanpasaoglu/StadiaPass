namespace StadiaPass.Domain.Abstractions;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs everything in <paramref name="operation"/> as one transaction: it all commits or none of it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The operation must be safe to run more than once.</b> A transient database failure - a dropped
    /// connection, a timeout, a failover - is retried, and the retry runs this delegate again from the top.
    /// The transaction rolls back in between, so anything the database did is undone; nothing else is.
    /// </para>
    /// <para>
    /// In practice that means: put database work in here, and keep everything else out. Moving an aggregate
    /// through a state transition, adding an entity to be tracked, appending to the outbox - none of those
    /// are undone by a rollback, so a second pass either throws (the seat is no longer the way the transition
    /// expects it) or quietly does the thing twice (two outbox rows, two confirmation mails). Do that work
    /// before the call and let the save inside carry it, which keeps it every bit as atomic.
    /// </para>
    /// </remarks>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
