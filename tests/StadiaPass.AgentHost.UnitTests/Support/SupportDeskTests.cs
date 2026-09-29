using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using StadiaPass.AgentHost.Policy;
using StadiaPass.AgentHost.Support;

namespace StadiaPass.AgentHost.UnitTests.Support;

/// <summary>
/// The desk is the workflow: route, then send the question to whichever assistant the route names - or to
/// both, at once, when the question asks both things. The models are fakes here; what is being pinned down
/// is who gets asked, who does not, and that "both" really means side by side rather than one after the other.
/// </summary>
public sealed class SupportDeskTests
{
    private static readonly PolicyPassage Refund = new(
        "iade-politikasi", "İade Politikası", "Maç iptal edildiğinde",
        "İade Politikası > Maç iptal edildiğinde\n\nBilet bedeli karta iade edilir.", 0.73);

    [Fact]
    public async Task Should_AskOnlyThePolicyAssistant_When_TheQuestionIsAboutARule()
    {
        var analyst = new FakeAnalyst("42 koltuk");
        var retriever = new FakeRetriever(Refund);
        var desk = Desk("policy", analyst, retriever);

        var answer = await desk.AskAsync("Maç iptal olursa param?", CancellationToken.None);

        answer.Topic.Should().Be("policy");
        answer.Replies.Should().ContainSingle().Which.Assistant.Should().Be(SupportDesk.PolicyName);
        answer.Replies[0].Sources.Should().ContainSingle().Which.Document.Should().Be("iade-politikasi");
        analyst.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_AskOnlyTheAnalyst_When_TheQuestionIsAboutAMatch()
    {
        var analyst = new FakeAnalyst("Derbide 42 boş koltuk var.");
        var retriever = new FakeRetriever(Refund);
        var desk = Desk("catalogue", analyst, retriever);

        var answer = await desk.AskAsync("Derbide kaç koltuk kaldı?", CancellationToken.None);

        answer.Topic.Should().Be("catalogue");
        answer.Replies.Should().ContainSingle().Which.Answer.Should().Be("Derbide 42 boş koltuk var.");
        retriever.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_UseTheOneReplyAsTheAnswer_AndNotMerge_When_OnlyOneAssistantAnswered()
    {
        // A merge costs a model call; with one reply there is nothing to merge, so it is never paid for.
        var mergeModel = new FakeChatClient("birleşik");
        var desk = Desk("policy", new FakeAnalyst("x"), new FakeRetriever(Refund), mergeModel);

        var answer = await desk.AskAsync("Maç iptal olursa param?", CancellationToken.None);

        answer.Answer.Should().Be("Karta iade edilir [1].");
        answer.Sources.Should().ContainSingle().Which.Document.Should().Be("iade-politikasi");
        mergeModel.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_MergeBothRepliesIntoOneAnswer_When_TheQuestionIsMixed()
    {
        var mergeModel = new FakeChatClient("Derbide 42 koltuk var. İptalde para karta iade edilir [1].");
        var desk = Desk("mixed", new FakeAnalyst("Derbide 42 koltuk var."), new FakeRetriever(Refund), mergeModel);

        var answer = await desk.AskAsync("Derbide kaç koltuk kaldı, maç iptal olursa param?", CancellationToken.None);

        answer.Answer.Should().Be("Derbide 42 koltuk var. İptalde para karta iade edilir [1].");
        answer.Sources.Should().ContainSingle().Which.Number.Should().Be(1);
        mergeModel.Received.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Should_AnswerFromTheDesk_AndAskNobody_When_TheQuestionIsNeither()
    {
        var analyst = new FakeAnalyst("x");
        var retriever = new FakeRetriever(Refund);
        var desk = Desk("other", analyst, retriever);

        var answer = await desk.AskAsync("Merhaba!", CancellationToken.None);

        answer.Topic.Should().Be("other");
        answer.Replies.Should().ContainSingle().Which.Answer.Should().Be(SupportDesk.OtherAnswer);
        analyst.Asked.Should().BeEmpty();
        retriever.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_AskBoth_AndReturnTheAnalystFirst_When_TheQuestionIsMixed()
    {
        var analyst = new FakeAnalyst("Derbide 42 boş koltuk var.");
        var retriever = new FakeRetriever(Refund);
        var desk = Desk("mixed", analyst, retriever);

        var answer = await desk.AskAsync("Derbide kaç koltuk kaldı, maç iptal olursa param?", CancellationToken.None);

        answer.Topic.Should().Be("mixed");
        answer.Replies.Select(reply => reply.Assistant).Should().Equal(SupportDesk.AnalystName, SupportDesk.PolicyName);
    }

    [Fact]
    public async Task Should_RunBothAssistantsAtTheSameTime_When_TheQuestionIsMixed()
    {
        // Each fake refuses to finish until the other has started. Run one after the other and the first
        // waits for a start that never comes; only running them side by side lets both through.
        var analystStarted = new TaskCompletionSource();
        var policyStarted = new TaskCompletionSource();

        var analyst = new FakeAnalyst("42 koltuk", started: analystStarted, waitFor: policyStarted);
        var retriever = new FakeRetriever(Refund, started: policyStarted, waitFor: analystStarted);
        var desk = Desk("mixed", analyst, retriever);

        var answer = await desk.AskAsync("Koltuk kaldı mı, iptalde param?", CancellationToken.None);

        answer.Replies.Should().HaveCount(2);
    }

    [Fact]
    public async Task Should_MaskPersonalData_BeforeTheRouterReadsTheQuestion()
    {
        var router = new FakeChatClient("policy");
        var desk = new SupportDesk(
            new SupportRouter(router),
            new FakeAnalyst("x"),
            Policy(new FakeRetriever(Refund)),
            new SupportMerger(new FakeChatClient("birleşik")));

        var answer = await desk.AskAsync("Mailim ali@test.com, koltuk ne kadar tutulur?", CancellationToken.None);

        router.Received.Should().ContainSingle().Which.Text.Should().NotContain("ali@test.com");
        answer.Question.Should().Contain("[redacted email address]");
    }

    private static SupportDesk Desk(
        string route,
        FakeAnalyst analyst,
        FakeRetriever retriever,
        FakeChatClient? mergeModel = null) =>
        new(
            new SupportRouter(new FakeChatClient(route)),
            analyst,
            Policy(retriever),
            new SupportMerger(mergeModel ?? new FakeChatClient("42 koltuk. Karta iade edilir [1].")));

    private static PolicyAssistant Policy(FakeRetriever retriever) =>
        new(retriever, new FakeChatClient("Karta iade edilir [1]."), NullLogger<PolicyAssistant>.Instance);

    private sealed class FakeAnalyst(string reply, TaskCompletionSource? started = null, TaskCompletionSource? waitFor = null)
        : IAnalyst
    {
        public List<string> Asked { get; } = [];

        public async Task<string> AskAsync(string question, CancellationToken cancellationToken)
        {
            Asked.Add(question);
            started?.TrySetResult();

            if (waitFor is not null)
            {
                await waitFor.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return reply;
        }
    }

    private sealed class FakeRetriever(PolicyPassage passage, TaskCompletionSource? started = null, TaskCompletionSource? waitFor = null)
        : IPolicyRetriever
    {
        public List<string> Asked { get; } = [];

        public async Task<IReadOnlyList<PolicyPassage>> RetrieveAsync(string question, CancellationToken cancellationToken)
        {
            Asked.Add(question);
            started?.TrySetResult();

            if (waitFor is not null)
            {
                await waitFor.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return [passage];
        }
    }

    private sealed class FakeChatClient(string answer) : IChatClient
    {
        public List<ChatMessage> Received { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            lock (Received)
            {
                Received.AddRange(messages);
            }

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
