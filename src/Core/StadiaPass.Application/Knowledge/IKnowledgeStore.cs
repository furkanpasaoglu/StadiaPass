namespace StadiaPass.Application.Knowledge;

/// <summary>A chunk together with the vector it was embedded to.</summary>
public sealed record EmbeddedKnowledgeChunk(KnowledgeChunk Chunk, float[] Embedding);

/// <summary>
/// What the store knows about a document it holds: which text it was cut from and which model read it.
/// </summary>
public sealed record KnowledgeDocumentState(string ContentHash, string Model);

/// <summary>
/// A chunk the store found near a question.
/// </summary>
/// <param name="Score">
/// Cosine similarity to the question, 1 being the same direction. For ordering and for the eyes of whoever
/// is debugging retrieval; not a threshold, because nothing about the number says whether the answer is in
/// the text - a question about parking is as near to the refund policy as the refund policy allows.
/// </param>
public sealed record KnowledgeHit(
    string Document,
    string Title,
    string Heading,
    string Text,
    double Score);

/// <summary>
/// Where the policy documents live once they have been cut up and embedded: chunks in, nearest chunks out.
/// </summary>
/// <remarks>
/// The application layer asks for the nearest few chunks to a vector and nothing more specific than that.
/// Whether the comparison happens in PostgreSQL, in memory or somewhere else is the implementation's
/// business; the corpus is a few pages, and the day it is not, the implementation changes and this does not.
/// </remarks>
public interface IKnowledgeStore
{
    /// <summary>What is held under <paramref name="document"/>, or <see langword="null"/> when nothing is.</summary>
    Task<KnowledgeDocumentState?> StateOfAsync(string document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts <paramref name="chunks"/> in under <paramref name="document"/>, taking out whatever was there.
    /// </summary>
    /// <remarks>
    /// Old chunks out and new ones in as one operation, or a document that shrank keeps a section it no
    /// longer has, and a load that fails half-way leaves a document with half its rules.
    /// </remarks>
    Task ReplaceDocumentAsync(
        string document,
        string contentHash,
        string model,
        IReadOnlyList<EmbeddedKnowledgeChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes out every document whose name is not in <paramref name="documents"/>, and says which went.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="ReplaceDocumentAsync"/>: replacing keeps a document that changed up
    /// to date, this keeps a document that was withdrawn from being answered from.
    /// </remarks>
    Task<IReadOnlyList<string>> RemoveDocumentsNotInAsync(
        IReadOnlyCollection<string> documents,
        CancellationToken cancellationToken = default);

    /// <summary>The <paramref name="limit"/> chunks nearest to <paramref name="query"/>, nearest first.</summary>
    Task<IReadOnlyList<KnowledgeHit>> NearestAsync(
        float[] query,
        int limit,
        CancellationToken cancellationToken = default);
}
