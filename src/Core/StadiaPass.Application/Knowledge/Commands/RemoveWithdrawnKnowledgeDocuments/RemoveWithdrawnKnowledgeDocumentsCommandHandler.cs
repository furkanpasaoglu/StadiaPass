using MediatR;
using Microsoft.Extensions.Logging;

namespace StadiaPass.Application.Knowledge.Commands.RemoveWithdrawnKnowledgeDocuments;

/// <summary>
/// Removes what the library no longer has - unless the library is empty.
/// </summary>
/// <remarks>
/// <para>
/// Loading a document only adds or replaces it, so a policy whose file was deleted would stay in the store
/// and the assistant would go on answering from a rule that has been withdrawn. This is the other half of
/// keeping the store equal to the folder.
/// </para>
/// <para>
/// An empty list is refused rather than obeyed. A folder with no documents in it is far more likely to be
/// a deployment that lost its files than a decision to withdraw every policy at once, and the cost of
/// guessing wrong is an assistant that knows nothing until somebody notices.
/// </para>
/// </remarks>
internal sealed partial class RemoveWithdrawnKnowledgeDocumentsCommandHandler(
    IKnowledgeStore store,
    ILogger<RemoveWithdrawnKnowledgeDocumentsCommandHandler> logger)
    : IRequestHandler<RemoveWithdrawnKnowledgeDocumentsCommand, RemoveWithdrawnKnowledgeDocumentsResultDto>
{
    public async Task<RemoveWithdrawnKnowledgeDocumentsResultDto> Handle(
        RemoveWithdrawnKnowledgeDocumentsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Current.Count == 0)
        {
            EmptyLibrary(logger);

            return new RemoveWithdrawnKnowledgeDocumentsResultDto([]);
        }

        var removed = await store.RemoveDocumentsNotInAsync(request.Current, cancellationToken);

        foreach (var document in removed)
        {
            Withdrawn(logger, document);
        }

        return new RemoveWithdrawnKnowledgeDocumentsResultDto(removed);
    }

    [LoggerMessage(
        EventId = 9010,
        Level = LogLevel.Information,
        Message = "Knowledge document {Document} is no longer in the library; its chunks were removed")]
    private static partial void Withdrawn(ILogger logger, string document);

    [LoggerMessage(
        EventId = 9011,
        Level = LogLevel.Warning,
        Message = "The library lists no documents; nothing was removed from the knowledge store")]
    private static partial void EmptyLibrary(ILogger logger);
}
