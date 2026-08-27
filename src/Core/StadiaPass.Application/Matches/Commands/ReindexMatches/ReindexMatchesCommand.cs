using MediatR;

namespace StadiaPass.Application.Matches.Commands.ReindexMatches;

/// <summary>
/// Builds the search index again from the database, from nothing.
/// </summary>
/// <remarks>
/// PostgreSQL is the source of truth and the index is derived from it, so this is the operation that makes
/// that claim true rather than merely stated: the index can be dropped, lost with its container, or left
/// behind by a change to its analyzers, and one call puts it back. It is also the only thing feeding the
/// index until the outbox projection is in place.
/// </remarks>
public sealed record ReindexMatchesCommand : IRequest<ReindexMatchesResultDto>;

public sealed record ReindexMatchesResultDto(int IndexedMatchCount);
