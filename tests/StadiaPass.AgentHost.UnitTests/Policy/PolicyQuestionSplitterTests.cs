using Microsoft.Extensions.AI;
using StadiaPass.AgentHost.Policy;

namespace StadiaPass.AgentHost.UnitTests.Policy;

/// <summary>
/// The splitter asks the model for the rule questions inside a message, one per line. The model decides what
/// the questions are; these tests pin down how its reply is read, because a model adds numbering, bullets and
/// blank lines however clearly it is told not to.
/// </summary>
public sealed class PolicyQuestionSplitterTests
{
    private const string Original = "Asıl soru?";

    [Fact]
    public void ReadQuestions_TakesOneQuestionPerLine()
    {
        var questions = PolicyQuestionSplitter.ReadQuestions(
            "Maç iptal olursa para ne olur?\nŞemsiye sokulabilir mi?", Original);

        questions.Should().Equal("Maç iptal olursa para ne olur?", "Şemsiye sokulabilir mi?");
    }

    [Fact]
    public void ReadQuestions_StripsNumberingBulletsAndBlankLines()
    {
        var questions = PolicyQuestionSplitter.ReadQuestions(
            "1. Maç iptal olursa para ne olur?\n\n- Şemsiye sokulabilir mi?\n2) Başkası adına bilet alınır mı?\n• Koltuk ne kadar tutulur?",
            Original);

        questions.Should().Equal(
            "Maç iptal olursa para ne olur?",
            "Şemsiye sokulabilir mi?",
            "Başkası adına bilet alınır mı?",
            "Koltuk ne kadar tutulur?");
    }

    [Fact]
    public void ReadQuestions_DropsTheSameQuestionAskedTwice()
    {
        var questions = PolicyQuestionSplitter.ReadQuestions(
            "Şemsiye sokulabilir mi?\nşemsiye sokulabilir mi?", Original);

        questions.Should().Equal("Şemsiye sokulabilir mi?");
    }

    [Fact]
    public void ReadQuestions_KeepsAtMostTheLimit()
    {
        var questions = PolicyQuestionSplitter.ReadQuestions("A?\nB?\nC?\nD?\nE?\nF?", Original);

        questions.Should().HaveCount(PolicyQuestionSplitter.MaxQuestions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    [InlineData(null)]
    public void ReadQuestions_FallsBackToTheOriginal_When_TheReplyHasNoQuestionInIt(string? reply)
    {
        PolicyQuestionSplitter.ReadQuestions(reply, Original).Should().Equal(Original);
    }

    [Fact]
    public async Task SplitAsync_SendsOnlyTheQuestion_UnderTheSplitterInstructions_AtTemperatureZero()
    {
        var model = new FakeChatClient("Şemsiye sokulabilir mi?");
        var splitter = new PolicyQuestionSplitter(model);

        var questions = await splitter.SplitAsync("Merhaba, şemsiye sokulabilir mi?", CancellationToken.None);

        questions.Should().Equal("Şemsiye sokulabilir mi?");
        model.Received.Should().ContainSingle().Which.Text.Should().Be("Merhaba, şemsiye sokulabilir mi?");
        model.Options!.Instructions.Should().Be(PolicyQuestionSplitter.Instructions);
        model.Options.Temperature.Should().Be(0f);
    }

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
