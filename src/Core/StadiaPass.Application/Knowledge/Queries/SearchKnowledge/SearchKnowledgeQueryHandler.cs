using MediatR;

namespace StadiaPass.Application.Knowledge.Queries.SearchKnowledge;

/// <summary>
/// Retrieval, and only retrieval: the question becomes a vector, the store says what sits nearest to it.
/// </summary>
/// <remarks>
/// There is always a nearest. A question about the weather gets three chunks back like any other, because a
/// vector cannot say "nothing here is about that" - it can only rank. Deciding whether the answer is
/// actually in the text is the reader's job, which is the model's, one layer up, with the chunks in front
/// of it. This handler does not try to guess with a threshold, because the similarity of a question that
/// has an answer and one that does not overlap too much for any number to separate them.
/// </remarks>
internal sealed class SearchKnowledgeQueryHandler(IKnowledgeEmbedder embedder, IKnowledgeStore store)
    : IRequestHandler<SearchKnowledgeQuery, KnowledgeSearchResultDto>
{
    public async Task<KnowledgeSearchResultDto> Handle(
        SearchKnowledgeQuery request,
        CancellationToken cancellationToken)
    {
        var question = request.Question.Trim();
        var vector = await embedder.EmbedAsync(question, cancellationToken);
        var hits = await store.NearestAsync(vector, request.Limit, cancellationToken);

        return new KnowledgeSearchResultDto(question, hits);
    }
}
