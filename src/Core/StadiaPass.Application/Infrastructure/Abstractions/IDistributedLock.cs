namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// A lock that holds across every instance of the application rather than just this process. It is a way of
/// not doing work that is about to be thrown away - the sale is made correct by the seat's concurrency token
/// in the database, not by this.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Takes the lock if it is free and gives up immediately if it is not: a customer waiting on a button is
    /// better told to try again than left watching a spinner while somebody else finishes.
    /// </summary>
    /// <param name="lease">
    /// How long the lock survives without being released. It is a safety net for a process that dies holding
    /// it, so it wants to be a little longer than the work it covers and no longer: until it runs out, the
    /// key is unavailable to everybody, including whoever crashed.
    /// </param>
    /// <returns>
    /// A handle to release by disposing, or <see langword="null"/> when somebody else holds the lock.
    /// A handle is also returned when the lock could not be reached at all - see the implementation for why
    /// that is deliberate - so <see langword="null"/> means "taken", never "unavailable".
    /// </returns>
    Task<IDistributedLockHandle?> TryAcquireAsync(
        string key,
        TimeSpan lease,
        CancellationToken cancellationToken = default);
}

/// <summary>The lock is held until this is disposed, or until its lease runs out - whichever comes first.</summary>
public interface IDistributedLockHandle : IAsyncDisposable;
