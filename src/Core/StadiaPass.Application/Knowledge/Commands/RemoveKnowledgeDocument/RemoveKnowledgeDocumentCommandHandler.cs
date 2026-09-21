using MediatR;
using StadiaPass.Application.Common.Exceptions;

namespace StadiaPass.Application.Knowledge.Commands.RemoveKnowledgeDocument;

/// <summary>
/// Removes an uploaded document. A library document is refused: it is owned by its file, the loader would
/// put it back on the next start, and a delete that undoes itself is worse than one that says no.
/// </summary>
internal sealed class RemoveKnowledgeDocumentCommandHandler(IKnowledgeStore store)
    : IRequestHandler<RemoveKnowledgeDocumentCommand>
{
    public async Task Handle(RemoveKnowledgeDocumentCommand request, CancellationToken cancellationToken)
    {
        var held = await store.StateOfAsync(request.Document, cancellationToken)
            ?? throw new NotFoundException("Knowledge document", request.Document);

        if (held.Origin == KnowledgeOrigin.Library)
        {
            throw new ConflictException(
                $"'{request.Document}' is part of the library that ships with the API. Remove its file "
                + "instead; the document leaves the store on the next start.");
        }

        await store.RemoveDocumentAsync(request.Document, cancellationToken);
    }
}
