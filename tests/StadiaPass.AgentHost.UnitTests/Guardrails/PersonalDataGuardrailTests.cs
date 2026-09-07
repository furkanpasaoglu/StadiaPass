using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using StadiaPass.AgentHost.Guardrails;

namespace StadiaPass.AgentHost.UnitTests.Guardrails;

/// <summary>
/// The guardrail sits between the agent and the model, and these tests are about the two directions it
/// has to hold: nothing personal reaches the model - or the telemetry and the logs on the way to it - and
/// nothing personal comes back out in an answer.
/// </summary>
/// <remarks>
/// The streaming test is the one that matters most. A model answers in fragments, and an address split
/// across two of them is invisible to anything that reads a fragment at a time; a filter that can be
/// defeated by where a chunk happens to end is decoration, not a control.
/// </remarks>
public sealed class PersonalDataGuardrailTests
{
    private const string ToolResultJson =
        "{\"asOfUtc\":\"2026-09-07T18:22:41.1234567+00:00\",\"result\":{\"matchId\":"
        + "\"d2b1f0a4-5c73-4e9a-9f21-0b6c8e5a1d33\",\"netRevenue\":1750.00,\"capacity\":742}}";

    [Fact]
    public async Task Should_RemoveThePersonalDatum_Before_TheModelEverSeesTheQuestion()
    {
        // Arrange
        var inner = new RecordingChatClient();
        var guardrail = Guarded(inner);

        // Act
        await guardrail.GetResponseAsync([new ChatMessage(ChatRole.User, "ahmet@example.com ne aldi?")]);

        // Assert
        inner.Received.Should().ContainSingle()
            .Which.Text.Should().Be("[redacted email address] ne aldi?");
    }

    [Fact]
    public async Task Should_PassAToolResultThroughUnchanged_When_ItCarriesNothingPersonal()
    {
        // Arrange - the shape a real reading arrives in: a match id, a timestamp, money and a capacity.
        var inner = new RecordingChatClient();
        var guardrail = Guarded(inner);
        var toolResult = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", ToolResultJson)]);

        // Act
        await guardrail.GetResponseAsync([toolResult]);

        // Assert
        inner.Received.Should().ContainSingle()
            .Which.Contents.OfType<FunctionResultContent>().Single()
            .Result.Should().Be(ToolResultJson);
    }

    [Fact]
    public async Task Should_RemoveThePersonalDatum_When_ATextToolResultCarriesOne()
    {
        // Arrange - no tool returns a customer today; the guardrail is what keeps that true if one does.
        var inner = new RecordingChatClient();
        var guardrail = Guarded(inner);
        var toolResult = new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call-1", "{\"holder\":\"ahmet@example.com\"}")]);

        // Act
        await guardrail.GetResponseAsync([toolResult]);

        // Assert
        inner.Received.Should().ContainSingle()
            .Which.Contents.OfType<FunctionResultContent>().Single()
            .Result.Should().Be("{\"holder\":\"[redacted email address]\"}");
    }

    [Fact]
    public async Task Should_HandOverTheVerySameMessages_When_ThereIsNothingToRemove()
    {
        // Arrange - the ordinary case is every question; it must not cost a rebuilt conversation.
        var inner = new RecordingChatClient();
        var guardrail = Guarded(inner);
        var asked = new ChatMessage(ChatRole.User, "Fenerbahce macinda kac koltuk kaldi?");

        // Act
        await guardrail.GetResponseAsync([asked]);

        // Assert
        inner.Received.Should().ContainSingle().Which.Should().BeSameAs(asked);
    }

    [Fact]
    public async Task Should_RemoveThePersonalDatum_When_TheModelPutsOneInItsAnswer()
    {
        // Arrange
        var inner = new RecordingChatClient
        {
            Answer = new ChatMessage(ChatRole.Assistant, "Bileti 0500 000 00 00 numarali musteri aldi.")
        };
        var guardrail = Guarded(inner);

        // Act
        var response = await guardrail.GetResponseAsync([new ChatMessage(ChatRole.User, "kim aldi?")]);

        // Assert
        response.Text.Should().Be("Bileti [redacted phone number] numarali musteri aldi.");
    }

    [Fact]
    public async Task Should_RemoveThePersonalDatum_When_ItIsSplitAcrossStreamedChunks()
    {
        // Arrange - no single fragment below contains an address; their concatenation does.
        var inner = new RecordingChatClient
        {
            Chunks = ["Musteri ", "ahmet", "@exam", "ple.com", " diye yaziyor."]
        };
        var guardrail = Guarded(inner);

        // Act
        var streamed = await CollectAsync(guardrail, "kim aldi?");

        // Assert
        string.Concat(streamed.Select(update => update.Text))
            .Should().Be("Musteri [redacted email address] diye yaziyor.");
    }

    [Fact]
    public async Task Should_LeaveACallForATool_Alone_When_TheModelAsksForOne()
    {
        // Arrange - redaction rewrites what the model said, never what it wants to do next.
        var inner = new RecordingChatClient
        {
            Chunks = ["Bakiyorum: ahmet@example.com"],
            StreamedCall = new FunctionCallContent("call-1", "get_match_revenue", new Dictionary<string, object?>
            {
                ["matchId"] = "d2b1f0a4-5c73-4e9a-9f21-0b6c8e5a1d33"
            })
        };
        var guardrail = Guarded(inner);

        // Act
        var streamed = await CollectAsync(guardrail, "ciro nedir?");

        // Assert
        var call = streamed.SelectMany(update => update.Contents).OfType<FunctionCallContent>().Single();
        call.Name.Should().Be("get_match_revenue");
        call.Arguments!["matchId"].Should().Be("d2b1f0a4-5c73-4e9a-9f21-0b6c8e5a1d33");
        string.Concat(streamed.Select(update => update.Text)).Should().Be("Bakiyorum: [redacted email address]");
    }

    private static PersonalDataGuardrail Guarded(IChatClient inner) =>
        new(
            inner,
            new GuardrailMetrics(new TestMeterFactory()),
            NullLogger<PersonalDataGuardrail>.Instance);

    private static async Task<List<ChatResponseUpdate>> CollectAsync(PersonalDataGuardrail client, string question)
    {
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, question)]))
        {
            updates.Add(update);
        }

        return updates;
    }

    /// <summary>Answers whatever it is told to, and keeps what it was asked so the test can look at it.</summary>
    private sealed class RecordingChatClient : IChatClient
    {
        public List<ChatMessage> Received { get; } = [];

        public ChatMessage Answer { get; init; } = new(ChatRole.Assistant, "Tamam.");

        public IReadOnlyList<string> Chunks { get; init; } = ["Tamam."];

        public FunctionCallContent? StreamedCall { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Received.AddRange(messages);

            return Task.FromResult(new ChatResponse(Answer));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Received.AddRange(messages);

            foreach (var chunk in Chunks)
            {
                await Task.Yield();

                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }

            if (StreamedCall is not null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [StreamedCall]);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
            _meters.Add(meter);

            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
