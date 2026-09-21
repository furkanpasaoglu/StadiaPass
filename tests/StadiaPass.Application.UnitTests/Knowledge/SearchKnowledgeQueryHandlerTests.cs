using NSubstitute;
using StadiaPass.Application.Knowledge;
using StadiaPass.Application.Knowledge.Queries.SearchKnowledge;

namespace StadiaPass.Application.UnitTests.Knowledge;

/// <summary>
/// Retrieval is two calls: the question becomes a vector, the store says which chunks sit nearest to it.
/// The handler adds nothing to that on purpose - what it must get right is handing the store the vector of
/// the question actually asked and the number of chunks actually wanted, and keeping the store's order.
/// </summary>
public sealed class SearchKnowledgeQueryHandlerTests
{
    private static readonly float[] QuestionVector = [0.1f, 0.9f];

    private readonly IKnowledgeEmbedder _embedder = Substitute.For<IKnowledgeEmbedder>();

    private readonly IKnowledgeStore _store = Substitute.For<IKnowledgeStore>();

    private readonly SearchKnowledgeQueryHandler _handler;

    public SearchKnowledgeQueryHandlerTests()
    {
        _handler = new SearchKnowledgeQueryHandler(_embedder, _store);
    }

    [Fact]
    public async Task Should_ReturnTheNearestChunksInTheStoresOrder()
    {
        var nearest = new[]
        {
            new KnowledgeHit("iade", "İade Politikası", "Maç iptal edildiğinde", "Karta iade edilir.", Score: 0.91),
            new KnowledgeHit("iade", "İade Politikası", "Müşterinin vazgeçmesi", "İade yapılmaz.", Score: 0.62)
        };

        _embedder.EmbedAsync("iptal olursa para?", Arg.Any<CancellationToken>()).Returns(QuestionVector);
        _store.NearestAsync(QuestionVector, 3, Arg.Any<CancellationToken>()).Returns(nearest);

        var result = await _handler.Handle(new SearchKnowledgeQuery("iptal olursa para?"), CancellationToken.None);

        result.Question.Should().Be("iptal olursa para?");
        result.Hits.Should().Equal(nearest);
    }

    [Fact]
    public async Task Should_AskTheStoreForAsManyChunksAsWereRequested()
    {
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(QuestionVector);
        _store.NearestAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _handler.Handle(new SearchKnowledgeQuery("kapılar", Limit: 5), CancellationToken.None);

        await _store.Received(1).NearestAsync(QuestionVector, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_EmbedTheQuestionWithItsSurroundingWhitespaceRemoved()
    {
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(QuestionVector);
        _store.NearestAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await _handler.Handle(new SearchKnowledgeQuery("  kapılar kaçta açılır  "), CancellationToken.None);

        await _embedder.Received(1).EmbedAsync("kapılar kaçta açılır", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_RefusesAnEmptyQuestionAndAnAbsurdLimit()
    {
        var validator = new SearchKnowledgeQueryValidator();

        validator.Validate(new SearchKnowledgeQuery("   ")).IsValid.Should().BeFalse();
        validator.Validate(new SearchKnowledgeQuery("kapılar", Limit: 0)).IsValid.Should().BeFalse();
        validator.Validate(new SearchKnowledgeQuery("kapılar", Limit: 11)).IsValid.Should().BeFalse();
        validator.Validate(new SearchKnowledgeQuery("kapılar", Limit: 3)).IsValid.Should().BeTrue();
    }
}
