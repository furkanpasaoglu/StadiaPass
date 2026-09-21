using MediatR;
using Microsoft.Extensions.Logging;
using StadiaPass.Application.Common.Exceptions;

namespace StadiaPass.Application.Knowledge.Commands.IndexKnowledgeDocument;

/// <summary>
/// Chunk, embed, replace - unless the store already holds exactly this.
/// </summary>
/// <remarks>
/// The check in front is what makes it safe to run on every start of the API. Embedding is one model call
/// per section, and a corpus that has not changed would otherwise be embedded again every morning for
/// nothing. Two things make a document stale: its text, and the model that read it - the second because
/// vectors from different models cannot be compared, so a text that has not changed still has to be done
/// again when the model has.
/// </remarks>
internal sealed partial class IndexKnowledgeDocumentCommandHandler(
    IKnowledgeEmbedder embedder,
    IKnowledgeStore store,
    ILogger<IndexKnowledgeDocumentCommandHandler> logger)
    : IRequestHandler<IndexKnowledgeDocumentCommand, IndexKnowledgeDocumentResultDto>
{
    public async Task<IndexKnowledgeDocumentResultDto> Handle(
        IndexKnowledgeDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var contentHash = KnowledgeContentHash.Of(request.Markdown);
        var held = await store.StateOfAsync(request.Document, cancellationToken);

        // A library document is owned by its file. An upload under the same name would last until the next
        // start, when the loader reads the file and puts the file's text back - so it is refused now, while
        // there is somebody to tell, rather than accepted and quietly undone later.
        if (held is not null && held.Origin == KnowledgeOrigin.Library && request.Origin == KnowledgeOrigin.Uploaded)
        {
            throw new ConflictException(
                $"'{request.Document}' is part of the library that ships with the API. Change its file, "
                + "or upload under a different name.");
        }

        if (held is not null
            && held.ContentHash == contentHash
            && held.Model == embedder.Model
            && held.Origin == request.Origin)
        {
            Unchanged(logger, request.Document);

            return new IndexKnowledgeDocumentResultDto(request.Document, ChunkCount: 0, Unchanged: true);
        }

        var chunks = KnowledgeChunker.Chunk(request.Document, request.Markdown);
        var embedded = new List<EmbeddedKnowledgeChunk>(chunks.Count);

        // One at a time rather than all at once: the corpus is a handful of documents of a handful of
        // sections, and a batch call is one more thing to explain for a wait nobody is sitting through.
        foreach (var chunk in chunks)
        {
            embedded.Add(new EmbeddedKnowledgeChunk(chunk, await embedder.EmbedAsync(chunk.Text, cancellationToken)));
        }

        await store.ReplaceDocumentAsync(
            request.Document,
            contentHash,
            embedder.Model,
            request.Origin,
            embedded,
            cancellationToken);

        Indexed(logger, request.Document, embedded.Count, embedder.Model);

        return new IndexKnowledgeDocumentResultDto(request.Document, embedded.Count, Unchanged: false);
    }

    [LoggerMessage(
        EventId = 9000,
        Level = LogLevel.Information,
        Message = "Knowledge document {Document} embedded into {ChunkCount} chunks with {Model}")]
    private static partial void Indexed(ILogger logger, string document, int chunkCount, string model);

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Debug,
        Message = "Knowledge document {Document} is unchanged; nothing embedded")]
    private static partial void Unchanged(ILogger logger, string document);
}
