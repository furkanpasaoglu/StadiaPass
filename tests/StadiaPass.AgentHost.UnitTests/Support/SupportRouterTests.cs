using Microsoft.Extensions.AI;
using StadiaPass.AgentHost.Support;

namespace StadiaPass.AgentHost.UnitTests.Support;

/// <summary>
/// The router asks the model for one word and turns it into a topic. The model is the part that can be
/// wrong about the question; these tests pin down the part that must never be wrong about the answer:
/// what is sent, and how a reply that is not one clean word is read.
/// </summary>
public sealed class SupportRouterTests
{
    [Theory]
    [InlineData("catalogue", SupportTopic.Catalogue)]
    [InlineData("policy", SupportTopic.Policy)]
    [InlineData("other", SupportTopic.Other)]
    [InlineData("mixed", SupportTopic.Mixed)]
    [InlineData("Mixed.", SupportTopic.Mixed)]
    [InlineData("Policy.", SupportTopic.Policy)]
    [InlineData("  CATALOGUE\n", SupportTopic.Catalogue)]
    [InlineData("Topic: policy", SupportTopic.Policy)]
    public void ReadTopic_UnderstandsTheWord_WhateverItIsWrappedIn(string reply, SupportTopic expected)
    {
        SupportRouter.ReadTopic(reply).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I am not sure")]
    [InlineData("policy or catalogue")]
    public void ReadTopic_FallsBackToOther_When_TheReplyNamesNoTopicOrMoreThanOne(string reply)
    {
        // Guessing between two answers would send a question to the wrong place with confidence; "other"
        // sends it nowhere and says so, which is the mistake a colleague can see.
        SupportRouter.ReadTopic(reply).Should().Be(SupportTopic.Other);
    }

    [Fact]
    public async Task RouteAsync_SendsOnlyTheQuestion_UnderTheRouterInstructions_AtTemperatureZero()
    {
        var model = new FakeChatClient("policy");
        var router = new SupportRouter(model);

        var topic = await router.RouteAsync("Maç iptal olursa param ne zaman gelir?", CancellationToken.None);

        topic.Should().Be(SupportTopic.Policy);
        model.Received.Should().ContainSingle()
            .Which.Text.Should().Be("Maç iptal olursa param ne zaman gelir?");
        model.Options!.Instructions.Should().Be(SupportRouter.Instructions);
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
