using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using StadiaPass.AgentHost.Policy;

namespace StadiaPass.AgentHost.UnitTests.Policy;

/// <summary>
/// Retrieve, then answer - and the order is fixed in code, not chosen by the model. What these pin down:
/// the model is handed the passages that were retrieved and nothing else, its answer comes back beside
/// the sources it was written from, and when nothing was retrieved the model is not asked at all - an
/// empty store must produce "no information", never an answer made up from an empty page.
/// </summary>
public sealed class PolicyAssistantTests
{
    private static readonly PolicyPassage Refund = new(
        "iade-politikasi", "İade Politikası", "Maç iptal edildiğinde",
        "İade Politikası > Maç iptal edildiğinde\n\nBilet bedeli karta iade edilir.", 0.73);

    [Fact]
    public async Task Should_HandTheModelThePassagesAndTheQuestion_AsOneUserMessage()
    {
        var model = new FakeChatClient("Karta iade edilir [1].");
        var assistant = Assistant(model, [Refund]);

        await assistant.AskAsync("Maç iptal olursa para?", CancellationToken.None);

        var message = model.Received.Should().ContainSingle().Subject;
        message.Role.Should().Be(ChatRole.User);
        message.Text.Should().Contain("Bilet bedeli karta iade edilir.").And.Contain("Maç iptal olursa para?");
    }

    [Fact]
    public async Task Should_RunTheModelUnderThePolicyInstructions_AtTemperatureZero()
    {
        var model = new FakeChatClient("Karta iade edilir [1].");
        var assistant = Assistant(model, [Refund]);

        await assistant.AskAsync("Maç iptal olursa para?", CancellationToken.None);

        model.Options.Should().NotBeNull();
        model.Options!.Instructions.Should().Be(PolicyAgent.Instructions);
        model.Options.Temperature.Should().Be(0f);
    }

    [Fact]
    public async Task Should_ReturnTheModelsAnswerBesideTheSourcesItWasWrittenFrom()
    {
        var model = new FakeChatClient("Karta iade edilir [1].");
        var assistant = Assistant(model, [Refund]);

        var answer = await assistant.AskAsync("Maç iptal olursa para?", CancellationToken.None);

        answer.Question.Should().Be("Maç iptal olursa para?");
        answer.Answer.Should().Be("Karta iade edilir [1].");
        answer.Sources.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new PolicySource(1, "iade-politikasi", "İade Politikası", "Maç iptal edildiğinde", 0.73));
    }

    [Fact]
    public async Task Should_NotAskTheModel_When_NothingWasRetrieved()
    {
        var model = new FakeChatClient("bir şey");
        var assistant = Assistant(model, []);

        var answer = await assistant.AskAsync("Otopark ücretli mi?", CancellationToken.None);

        model.Received.Should().BeEmpty();
        answer.Answer.Should().Be(PolicyAssistant.NoPassagesAnswer);
        answer.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_MaskAPersonalDatum_Before_ItIsUsedToRetrieve()
    {
        // The chat client's guardrail masks what reaches the model, but retrieval runs before the chat
        // client: the question goes to the MCP server, the API and the embedding model as typed. So the
        // assistant masks first, and everything downstream - retrieval, prompt, answer - sees the placeholder.
        var retriever = new RecordingRetriever();
        var model = new FakeChatClient("İade yapılmaz [1].");
        var assistant = new PolicyAssistant(retriever, model, NullLogger<PolicyAssistant>.Instance);

        var answer = await assistant.AskAsync("ahmet@example.com gelemeyecek, iade var mı?", CancellationToken.None);

        retriever.Asked.Should().Be("[redacted email address] gelemeyecek, iade var mı?");
        answer.Question.Should().NotContain("ahmet@example.com");
        model.Received.Should().ContainSingle().Which.Text.Should().NotContain("ahmet@example.com");
    }

    private sealed class RecordingRetriever : IPolicyRetriever
    {
        public string? Asked { get; private set; }

        public Task<IReadOnlyList<PolicyPassage>> RetrieveAsync(string question, CancellationToken cancellationToken)
        {
            Asked = question;

            return Task.FromResult<IReadOnlyList<PolicyPassage>>([Refund]);
        }
    }

    private static PolicyAssistant Assistant(IChatClient model, IReadOnlyList<PolicyPassage> passages) =>
        new(new FixedRetriever(passages), model, NullLogger<PolicyAssistant>.Instance);

    private sealed class FixedRetriever(IReadOnlyList<PolicyPassage> passages) : IPolicyRetriever
    {
        public Task<IReadOnlyList<PolicyPassage>> RetrieveAsync(string question, CancellationToken cancellationToken) =>
            Task.FromResult(passages);
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
