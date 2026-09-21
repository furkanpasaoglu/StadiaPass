namespace StadiaPass.McpServer.Api;

// Wire-shape mirrors of the WebAPI's knowledge search response, for the same reason the catalogue
// models are copies: this server is a client of the API and does not reference the Application assembly.

public sealed record PolicySearchResult(string Question, IReadOnlyList<PolicyPassage> Hits);

/// <param name="Score">
/// Cosine similarity to the question, 1 being identical. For ordering and for the reader; not a
/// threshold - nothing about the number says whether the answer is in the text.
/// </param>
public sealed record PolicyPassage(
    string Document,
    string Title,
    string Heading,
    string Text,
    double Score);
