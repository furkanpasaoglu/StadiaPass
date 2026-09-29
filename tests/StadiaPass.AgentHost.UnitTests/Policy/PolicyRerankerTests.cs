using Microsoft.Extensions.AI;
using StadiaPass.AgentHost.Policy;

namespace StadiaPass.AgentHost.UnitTests.Policy;

/// <summary>
/// The reranker asks the model which of the retrieved passages help, best first. The model's judgement is
/// measured by the evals; these tests pin down what is done with its reply - including every way the reply can
/// be useless, all of which must leave retrieval exactly as it was before reranking existed.
/// </summary>
public sealed class PolicyRerankerTests
{
    private static readonly PolicyPassage[] Candidates =
    [
        Passage("A"), Passage("B"), Passage("C"), Passage("D"), Passage("E")
    ];

    [Fact]
    public async Task RerankAsync_PutsThePassagesTheModelChoseFirst_InItsOrder()
    {
        var reranker = new PolicyReranker(new FakeChatClient("4, 1, 5"));

        var chosen = await reranker.RerankAsync("Soru?", Candidates, take: 3, CancellationToken.None);

        chosen.Select(passage => passage.Heading).Should().Equal("D", "A", "E");
    }

    [Fact]
    public async Task RerankAsync_FillsUpInRetrievalOrder_When_TheModelChoseFewer()
    {
        // Three passages is a deliberate choice - the section that answers, and the neighbours that carry its
        // exception. A reranker that names only one does not get to take the neighbours away.
        var reranker = new PolicyReranker(new FakeChatClient("4"));

        var chosen = await reranker.RerankAsync("Soru?", Candidates, take: 3, CancellationToken.None);

        chosen.Select(passage => passage.Heading).Should().Equal("D", "A", "B");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("none of them")]
    [InlineData("9, 12")]
    public async Task RerankAsync_KeepsRetrievalOrder_When_TheReplyNamesNoUsablePassage(string reply)
    {
        var reranker = new PolicyReranker(new FakeChatClient(reply));

        var chosen = await reranker.RerankAsync("Soru?", Candidates, take: 3, CancellationToken.None);

        chosen.Select(passage => passage.Heading).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task RerankAsync_IgnoresRepeatsAndOutOfRangeNumbers()
    {
        var reranker = new PolicyReranker(new FakeChatClient("2, 2, 7, 3"));

        var chosen = await reranker.RerankAsync("Soru?", Candidates, take: 3, CancellationToken.None);

        chosen.Select(passage => passage.Heading).Should().Equal("B", "C", "A");
    }

    [Fact]
    public async Task RerankAsync_DoesNotAskTheModel_When_ThereIsNothingToChooseBetween()
    {
        var model = new FakeChatClient("1");
        var reranker = new PolicyReranker(model);

        var chosen = await reranker.RerankAsync("Soru?", [Passage("A"), Passage("B")], take: 3, CancellationToken.None);

        chosen.Select(passage => passage.Heading).Should().Equal("A", "B");
        model.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task RerankAsync_ShowsTheModelTheQuestionAndEveryPassageNumbered_AtTemperatureZero()
    {
        var model = new FakeChatClient("1");
        var reranker = new PolicyReranker(model);

        await reranker.RerankAsync("Şemsiye sokulur mu?", Candidates, take: 3, CancellationToken.None);

        var prompt = model.Received.Should().ContainSingle().Subject.Text;
        prompt.Should().Contain("Şemsiye sokulur mu?").And.Contain("[1]").And.Contain("[5]").And.Contain("E metni.");
        model.Options!.Instructions.Should().Be(PolicyReranker.Instructions);
        model.Options.Temperature.Should().Be(0f);
    }

    private static PolicyPassage Passage(string heading) =>
        new("belge", "Belge", heading, $"Belge > {heading}\n\n{heading} metni.", 0.7);

    private sealed class FakeChatClient(string answer) : IChatClient
    {
        public List<ChatMessage> Received { get; } = [];

        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Received.AddRange(messages);
            Options = options;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
