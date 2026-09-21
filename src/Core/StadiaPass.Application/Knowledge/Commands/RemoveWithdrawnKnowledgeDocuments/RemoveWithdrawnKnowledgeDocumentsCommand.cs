using MediatR;

namespace StadiaPass.Application.Knowledge.Commands.RemoveWithdrawnKnowledgeDocuments;

/// <summary>Takes out of the store every document that is no longer part of the library.</summary>
/// <param name="Current">The names of the documents the library holds now. Everything else goes.</param>
public sealed record RemoveWithdrawnKnowledgeDocumentsCommand(IReadOnlyCollection<string> Current)
    : IRequest<RemoveWithdrawnKnowledgeDocumentsResultDto>;

/// <param name="Removed">The names of the documents that were taken out. Empty when nothing was.</param>
public sealed record RemoveWithdrawnKnowledgeDocumentsResultDto(IReadOnlyList<string> Removed);
