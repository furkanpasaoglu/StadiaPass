using MediatR;

namespace StadiaPass.Application.Knowledge.Queries.SearchKnowledge;

/// <summary>The sections of the policy documents that sit nearest to a question, nearest first.</summary>
/// <param name="Limit">How many to bring back. Three is what an answer is usually written from.</param>
public sealed record SearchKnowledgeQuery(string Question, int Limit = 3) : IRequest<KnowledgeSearchResultDto>;

public sealed record KnowledgeSearchResultDto(string Question, IReadOnlyList<KnowledgeHit> Hits);
