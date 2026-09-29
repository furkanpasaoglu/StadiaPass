using Microsoft.Extensions.AI;
using StadiaPass.AgentHost.Support;

namespace StadiaPass.AgentHost.UnitTests.Support;

/// <summary>
/// The merger turns two replies into one. What these tests pin down is the part that must not depend on the
/// model: what it is shown, and the citation check that decides whether its merge is used at all.
/// </summary>
public sealed class SupportMergerTests
{
    [Fact]
    public async Task MergeAsync_ShowsTheModelTheQuestionAndBothReplies_UnderTheMergerInstructions_AtTemperatureZero()
    {
        var model = new FakeChatClient("Birleşik cevap [1].");
        var merger = new SupportMerger(model);

        await merger.MergeAsync("Soru?", "Analistin cevabı.", "Kuralın cevabı [1].", CancellationToken.None);

        var message = model.Received.Should().ContainSingle().Subject;
        message.Text.Should().Contain("Soru?").And.Contain("Analistin cevabı.").And.Contain("Kuralın cevabı [1].");
        model.Options!.Instructions.Should().Be(SupportMerger.Instructions);
        model.Options.Temperature.Should().Be(0f);
    }

    [Fact]
    public async Task MergeAsync_ReturnsTheMerge_When_EveryCitationSurvived()
    {
        var merger = new SupportMerger(new FakeChatClient("Derbide 42 koltuk var. İptalde para karta döner [1]."));

        var merged = await merger.MergeAsync("Soru?", "42 koltuk var.", "Para karta döner [1].", CancellationToken.None);

        merged.Should().Be("Derbide 42 koltuk var. İptalde para karta döner [1].");
    }

    [Fact]
    public async Task MergeAsync_FallsBackToBothRepliesAsTheyWere_When_ACitationWasLost()
    {
        // The policy reply cites [1] and [2]; the merge kept only [1]. A sentence that lost its source is a
        // sentence nobody can check, so the merge is thrown away rather than shown.
        var merger = new SupportMerger(new FakeChatClient("42 koltuk var. Para karta döner [1]."));

        var merged = await merger.MergeAsync(
            "Soru?", "42 koltuk var.", "Para karta döner [1]. Banka birkaç gün sürebilir [2].", CancellationToken.None);

        merged.Should().Be("42 koltuk var.\n\nPara karta döner [1]. Banka birkaç gün sürebilir [2].");
    }

    [Fact]
    public async Task MergeAsync_FallsBackToBothReplies_When_TheModelReturnsNothing()
    {
        var merger = new SupportMerger(new FakeChatClient("   "));

        var merged = await merger.MergeAsync("Soru?", "42 koltuk var.", "Para karta döner [1].", CancellationToken.None);

        merged.Should().Be("42 koltuk var.\n\nPara karta döner [1].");
    }

    [Theory]
    [InlineData("Para karta döner [1]. Banka sürebilir [2].", new[] { 1, 2 })]
    [InlineData("Belgelerde bilgi yok.", new int[0])]
    [InlineData("[3] ve [1] ve yine [3]", new[] { 1, 3 })]
    public void CitationsOf_FindsEveryNumberInSquareBrackets(string text, int[] expected)
    {
        SupportMerger.CitationsOf(text).Should().BeEquivalentTo(expected);
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
