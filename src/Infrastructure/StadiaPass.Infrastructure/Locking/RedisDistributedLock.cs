using Microsoft.Extensions.Logging;
using StadiaPass.Application.Infrastructure.Abstractions;
using StackExchange.Redis;

namespace StadiaPass.Infrastructure.Locking;

/// <summary>
/// Redis is single threaded, so <c>SET key value NX PX</c> settles who holds a key with no negotiation and no
/// second round trip. That is the whole mechanism.
/// </summary>
internal sealed partial class RedisDistributedLock(
    IConnectionMultiplexer connection,
    ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        string key,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The value is not decoration: it is what proves at release time that the lock still belongs to this
        // caller. Without it the release is a plain delete, and a plain delete cannot tell its own lock from
        // the one somebody else took after this lease quietly expired.
        var token = Guid.CreateVersion7().ToString("N");

        try
        {
            var acquired = await connection.GetDatabase()
                .StringSetAsync(key, token, lease, When.NotExists);

            return acquired ? new Handle(connection, logger, key, token) : null;
        }
        catch (RedisException exception)
        {
            // Deliberately fails open. This lock saves a Stripe round trip and a refund; it is not what makes
            // a sale correct - the seat's concurrency token is, and that lives in the database. Refusing every
            // purchase because Redis is having a bad afternoon would turn a cache outage into a sales outage.
            LockUnreachable(logger, key, exception);

            return Unguarded.Instance;
        }
    }

    [LoggerMessage(
        EventId = 6200,
        Level = LogLevel.Warning,
        Message = "Redis could not be reached for lock {LockKey}; carrying on without it")]
    private static partial void LockUnreachable(ILogger logger, string lockKey, Exception exception);

    [LoggerMessage(
        EventId = 6201,
        Level = LogLevel.Warning,
        Message = "Lock {LockKey} could not be released; it will fall away when its lease runs out")]
    private static partial void ReleaseFailed(ILogger logger, string lockKey, Exception exception);

    private sealed class Handle(
        IConnectionMultiplexer connection,
        ILogger logger,
        string key,
        string token) : IDistributedLockHandle
    {
        /// <summary>
        /// Compare and delete, in one shot, because Redis runs a script without interleaving anything else.
        /// Reading the value and then deleting it would leave a gap in which the lease could expire and
        /// somebody else could take the key - and this release would then throw away their lock.
        /// </summary>
        private const string ReleaseScript =
            "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

        public async ValueTask DisposeAsync()
        {
            try
            {
                await connection.GetDatabase()
                    .ScriptEvaluateAsync(ReleaseScript, [key], [token]);
            }
            catch (RedisException exception)
            {
                // The lease expires on its own, so the worst of this is one seat nobody can buy for a minute.
                ReleaseFailed(logger, key, exception);
            }
        }
    }

    /// <summary>What a caller gets when Redis could not be reached: a handle with nothing to release.</summary>
    private sealed class Unguarded : IDistributedLockHandle
    {
        public static readonly Unguarded Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
