using MediatR;

namespace StadiaPass.Application.Knowledge.Commands.IndexKnowledgeDocument;

/// <summary>Cuts a policy document into sections, embeds each one and puts them in the store.</summary>
/// <param name="Document">The name the document is held under; loading it again replaces it.</param>
/// <param name="Markdown">The document itself: a title, second-level headings, paragraphs and lists.</param>
public sealed record IndexKnowledgeDocumentCommand(string Document, string Markdown)
    : IRequest<IndexKnowledgeDocumentResultDto>;

/// <param name="Unchanged">
/// <see langword="true"/> when the store already held this text as read by this model, and nothing was done.
/// </param>
public sealed record IndexKnowledgeDocumentResultDto(string Document, int ChunkCount, bool Unchanged);
