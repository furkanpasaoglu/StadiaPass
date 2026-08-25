using StadiaPass.Application.Payments.Events;

namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// Where a refund that is owed gets written down.
/// </summary>
/// <remarks>
/// This is not <see cref="IOutbox"/> with a different name, and the difference is the whole point. The outbox
/// stages a message for the caller's own transaction; this is used at the exact moment that transaction has
/// just been rolled back, so the caller's unit of work is holding a seat, a ticket and a sale that must never
/// reach the database. Writing through it would save the very sale that failed.
/// <para>
/// So this writes on its own - its own unit of work, its own connection - and commits by itself. That is also
/// what makes it useful when the failure was the database: a fresh connection may work where the poisoned one
/// did not.
/// </para>
/// </remarks>
public interface IRefundLedger
{
    /// <summary>
    /// Records that the money is owed. Returns <see langword="false"/> rather than throwing when even this
    /// cannot be written: the caller is already on its way to an error and has a refund to fall back on, and
    /// replacing that error with this one would hide what actually went wrong.
    /// </summary>
    Task<bool> RecordAsync(RefundOwedEvent refund, CancellationToken cancellationToken = default);
}
