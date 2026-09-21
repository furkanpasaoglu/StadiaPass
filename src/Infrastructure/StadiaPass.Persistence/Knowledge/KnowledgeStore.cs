using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using StadiaPass.Application.Knowledge;

namespace StadiaPass.Persistence.Knowledge;

/// <summary>
/// The policy chunks in PostgreSQL, compared with pgvector.
/// </summary>
/// <remarks>
/// The nearest-neighbour query is one <c>ORDER BY embedding &lt;=&gt; @question LIMIT n</c>, cosine
/// distance computed by the extension. The store reports it as a similarity (one minus the distance) because
/// "0.91" reads as "close" and "0.09" does not, and whoever is debugging retrieval is reading these by eye.
/// </remarks>
internal sealed class KnowledgeStore(StadiaPassDbContext context) : IKnowledgeStore
{
    public async Task<KnowledgeDocumentState?> StateOfAsync(
        string document,
        CancellationToken cancellationToken = default)
    {
        // Every row of a document carries the same hash and model, so any one of them answers.
        var row = await context.KnowledgeChunks
            .AsNoTracking()
            .Where(chunk => chunk.Document == document)
            .Select(chunk => new { chunk.ContentHash, chunk.Model })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new KnowledgeDocumentState(row.ContentHash, row.Model);
    }

    public async Task ReplaceDocumentAsync(
        string document,
        string contentHash,
        string model,
        IReadOnlyList<EmbeddedKnowledgeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var rows = chunks.Select(embedded => new KnowledgeChunkRow
        {
            Id = Guid.CreateVersion7(),
            Document = document,
            Title = embedded.Chunk.Title,
            Heading = embedded.Chunk.Heading,
            Text = embedded.Chunk.Text,
            Position = embedded.Chunk.Position,
            ContentHash = contentHash,
            Model = model,
            Embedding = new Vector(embedded.Embedding)
        }).ToArray();

        // Old rows out and new rows in under one transaction, so a reader never sees a document with half
        // its sections and a load that fails leaves the previous version in place rather than nothing.
        // Wrapped in the execution strategy the way every transaction here is: the connection retries on a
        // transient failure, and a transaction it opened itself is the only kind it knows how to run again.
        await context.Database.CreateExecutionStrategy().ExecuteAsync(
            cancellationToken,
            async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);

                await context.KnowledgeChunks
                    .Where(chunk => chunk.Document == document)
                    .ExecuteDeleteAsync(token);

                context.KnowledgeChunks.AddRange(rows);

                await context.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            });
    }

    public async Task<IReadOnlyList<string>> RemoveDocumentsNotInAsync(
        IReadOnlyCollection<string> documents,
        CancellationToken cancellationToken = default)
    {
        var kept = documents.ToArray();

        // Asked for by name first, so the caller can be told what went; a bare delete would only say how
        // many rows it removed, and "three rows" is not something anybody can check against the folder.
        var withdrawn = await context.KnowledgeChunks
            .AsNoTracking()
            .Where(chunk => !kept.Contains(chunk.Document))
            .Select(chunk => chunk.Document)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (withdrawn.Count == 0)
        {
            return withdrawn;
        }

        await context.KnowledgeChunks
            .Where(chunk => withdrawn.Contains(chunk.Document))
            .ExecuteDeleteAsync(cancellationToken);

        return withdrawn;
    }

    public async Task<IReadOnlyList<KnowledgeHit>> NearestAsync(
        float[] query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var question = new Vector(query);

        var nearest = await context.KnowledgeChunks
            .AsNoTracking()
            .Select(chunk => new
            {
                chunk.Document,
                chunk.Title,
                chunk.Heading,
                chunk.Text,
                Distance = chunk.Embedding.CosineDistance(question)
            })
            .OrderBy(chunk => chunk.Distance)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. nearest.Select(chunk =>
            new KnowledgeHit(chunk.Document, chunk.Title, chunk.Heading, chunk.Text, Score: 1 - chunk.Distance))];
    }
}
