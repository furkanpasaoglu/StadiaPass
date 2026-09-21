namespace StadiaPass.Application.Knowledge;

/// <summary>
/// Turns a piece of text into the vector that says what it is about.
/// </summary>
/// <remarks>
/// The same model has to embed the question and the chunks it is compared against; vectors from two
/// different models are not in the same space and comparing them is comparing nothing. So the model's
/// name is part of the contract, and the store records it beside every vector to know when the
/// vectors it holds have gone stale.
/// </remarks>
public interface IKnowledgeEmbedder
{
    /// <summary>The model doing the embedding, as its provider names it.</summary>
    string Model { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
