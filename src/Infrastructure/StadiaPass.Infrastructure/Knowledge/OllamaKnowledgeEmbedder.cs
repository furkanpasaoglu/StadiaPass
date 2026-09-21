using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using StadiaPass.Application.Knowledge;

namespace StadiaPass.Infrastructure.Knowledge;

/// <summary>
/// Ollama's embedding endpoint, called directly.
/// </summary>
/// <remarks>
/// One POST with a model name and a text, one array of floats back. That is the whole protocol, and an
/// SDK would add a package and an abstraction to something that fits in a screen; the agent host uses one
/// because it also talks chat, tools and streaming, none of which happens here.
/// </remarks>
internal sealed class OllamaKnowledgeEmbedder(HttpClient client, IOptions<KnowledgeOptions> options)
    : IKnowledgeEmbedder
{
    public string Model => options.Value.EmbeddingModel;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/embed",
            new EmbedRequest(Model, text),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Ollama answered the embedding request with an empty body.");

        return body.Embeddings is [var embedding, ..]
            ? embedding
            : throw new InvalidOperationException($"Ollama returned no embedding for the text from {Model}.");
    }

    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);

    private sealed record EmbedResponse(
        [property: JsonPropertyName("embeddings")] float[][] Embeddings);
}
