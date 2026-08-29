namespace StadiaPass.Application.Matches.Events;

/// <summary>
/// A fixture has appeared or changed in a way the search index would notice.
/// </summary>
/// <remarks>
/// <para>
/// The identifier and nothing else. A message carrying the fixture's teams and venue would be a second copy
/// of the truth travelling separately from the first, and two deliveries arriving out of order would then
/// leave the index holding the older one. Sending only the identifier makes the consumer re-read the row it
/// names, so the last write in is whatever the database says at the moment it is read - which is the answer
/// that cannot be wrong.
/// </para>
/// <para>
/// Written to the outbox in the same transaction as the fixture itself, so a match that is committed is a
/// match the index will hear about, and a match that rolled back is one it will not.
/// </para>
/// </remarks>
public sealed record MatchCatalogueChangedEvent(Guid MatchId);

/// <summary>
/// A fixture has been called off. Carries only what the far side cannot look up for itself.
/// </summary>
/// <remarks>
/// One message for the fixture rather than one per ticket, on purpose: the cancellation has to commit in a
/// single small transaction so selling stops at once, and working out which tickets owe money is a query the
/// consumer can run for itself a moment later.
/// </remarks>
public sealed record MatchCancelledEvent(Guid MatchId, string Reason, DateTimeOffset CancelledAtUtc);
