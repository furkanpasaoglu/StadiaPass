namespace StadiaPass.Application.Infrastructure.Abstractions;

/// <summary>
/// Where a message waits between being decided and being sent.
/// </summary>
/// <remarks>
/// Publishing to a broker and writing to a database cannot be made one atomic act, so doing both leaves two
/// ways to be wrong: a ticket sold that nobody is ever told about, or a confirmation mail for a sale that
/// rolled back. Writing the message into the same transaction as the sale removes the choice - either both
/// land or neither does - and a worker carries it to the broker afterwards.
/// </remarks>
public interface IOutbox
{
    /// <summary>
    /// Holds the message ready to be written. It is not saved here: it goes to the database with whatever
    /// else the caller's transaction is writing, which is the entire point.
    /// </summary>
    void Enqueue(object message);
}
