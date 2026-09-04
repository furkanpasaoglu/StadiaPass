namespace StadiaPass.McpServer.Tools;

/// <summary>
/// A tool result and the instant it was read. Every tool here answers about figures that move - seats
/// sell, refunds land - so what came back is a reading taken at a moment, not a standing fact.
/// </summary>
/// <remarks>
/// <para>
/// The timestamp exists because of a real failure. A conversation is a cache with no expiry: an earlier
/// tool result stays in the history, the model has no clock of its own, and "every number must come from
/// a tool result" is satisfied to the letter by a result taken ten minutes and one sale ago. Asked the
/// same question twice in one conversation, the analyst answered the second time from the first answer -
/// no tool call, no API call, a figure that had already changed.
/// </para>
/// <para>
/// Stamping the reading does not fix that on its own; the instruction telling the analyst to read again
/// does. What this adds is the thing the model could not otherwise know or say: which moment a figure
/// belongs to, so an answer can carry it and a stale one can be recognised as stale rather than sound
/// exactly like a fresh one.
/// </para>
/// </remarks>
/// <param name="AsOfUtc">When the server read the figures below - not when the question was asked.</param>
/// <param name="Result">The answer itself, in the shape that tool always returns.</param>
public sealed record Reading<T>(DateTimeOffset AsOfUtc, T Result);
