using MediatR;

namespace StadiaPass.Application.Knowledge.Commands.RemoveKnowledgeDocument;

/// <summary>Takes an uploaded policy document out of the store, so it stops being answered from.</summary>
/// <param name="Document">The name the document was uploaded under.</param>
public sealed record RemoveKnowledgeDocumentCommand(string Document) : IRequest;
