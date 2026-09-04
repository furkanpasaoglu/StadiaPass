namespace StadiaPass.Domain.Tickets;

/// <summary>
/// What one fixture's tickets add up to, in one state and one currency: how many there are and what they
/// came to.
/// </summary>
/// <remarks>
/// Lines rather than a single total, and counted by the database rather than in memory. A sold-out fixture
/// in a large venue has tens of thousands of tickets, and pulling them into a change tracker to add up a
/// number nobody is going to modify would be the most expensive way to answer the cheapest question. The
/// split by status is what lets the caller state the rule out loud - a refunded ticket is not revenue -
/// instead of trusting whoever wrote the query to have remembered it.
/// </remarks>
public sealed record TicketRevenueLine(TicketStatus Status, string Currency, int Count, decimal Amount);
