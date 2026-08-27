using MediatR;

namespace StadiaPass.Application.Matches.Commands.IndexMatch;

/// <summary>
/// Writes one fixture into the search index as the database currently has it.
/// </summary>
/// <remarks>
/// The projection that keeps the index level with PostgreSQL between rebuilds. It carries no fixture data of
/// its own on purpose - see <see cref="Events.MatchCatalogueChangedEvent"/> - so running it twice, or out of
/// order with itself, lands the same document either way.
/// </remarks>
public sealed record IndexMatchCommand(Guid MatchId) : IRequest;
