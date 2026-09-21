namespace StadiaPass.AgentHost.Policy;

/// <summary>
/// One section of a policy document, as retrieval handed it over: where it came from, what it says, and
/// how near it sat to the question.
/// </summary>
public sealed record PolicyPassage(
    string Document,
    string Title,
    string Heading,
    string Text,
    double Score);

/// <summary>
/// Where the passages come from. Behind an interface so the assistant can be scored without a network:
/// in production it is the MCP server's search tool, in a test it is a list.
/// </summary>
public interface IPolicyRetriever
{
    Task<IReadOnlyList<PolicyPassage>> RetrieveAsync(string question, CancellationToken cancellationToken);
}

/// <summary>A passage the answer was written from, by the number the answer cites it under.</summary>
public sealed record PolicySource(int Number, string Document, string Title, string Heading, double Score);

/// <summary>What the assistant hands back: the answer and the passages it had in front of it.</summary>
public sealed record PolicyAnswer(string Question, string Answer, IReadOnlyList<PolicySource> Sources);
