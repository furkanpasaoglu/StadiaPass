using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Knowledge;
using StadiaPass.Application.Knowledge.Commands.IndexKnowledgeDocument;

namespace StadiaPass.Application.UnitTests.Knowledge;

/// <summary>
/// Loading a document is chunk, embed, replace - and the thing worth pinning down is when it must NOT
/// happen. Embedding costs a model call per section on every start of the API, so a document that has not
/// changed has to be left alone, and a document embedded by a different model has to be done again even
/// though its text is the same, because two models' vectors cannot be compared with each other.
/// </summary>
public sealed class IndexKnowledgeDocumentCommandHandlerTests
{
    private const string Markdown = """
        # İade Politikası

        ## Maç iptal edildiğinde

        Karta iade edilir.

        ## Müşterinin vazgeçmesi

        İade yapılmaz.
        """;

    private readonly IKnowledgeEmbedder _embedder = Substitute.For<IKnowledgeEmbedder>();

    private readonly IKnowledgeStore _store = Substitute.For<IKnowledgeStore>();

    private readonly IndexKnowledgeDocumentCommandHandler _handler;

    public IndexKnowledgeDocumentCommandHandlerTests()
    {
        _embedder.Model.Returns("bge-m3");
        _embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => VectorFor(call.Arg<string>()));

        _handler = new IndexKnowledgeDocumentCommandHandler(
            _embedder,
            _store,
            NullLogger<IndexKnowledgeDocumentCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_EmbedEverySectionAndReplaceTheDocument_When_ItIsNew()
    {
        _store.StateOfAsync("iade", Arg.Any<CancellationToken>()).Returns((KnowledgeDocumentState?)null);

        var result = await _handler.Handle(new IndexKnowledgeDocumentCommand("iade", Markdown), CancellationToken.None);

        result.Should().Be(new IndexKnowledgeDocumentResultDto("iade", ChunkCount: 2, Unchanged: false));

        await _store.Received(1).ReplaceDocumentAsync(
            "iade",
            KnowledgeContentHash.Of(Markdown),
            "bge-m3",
            KnowledgeOrigin.Library,
            Arg.Is<IReadOnlyList<EmbeddedKnowledgeChunk>>(chunks =>
                chunks.Count == 2
                && chunks.All(chunk => chunk.Embedding.SequenceEqual(VectorFor(chunk.Chunk.Text)))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_EmbedTheTextTheModelWillRead_NotTheBareParagraph()
    {
        // The vector has to be of the same words the model is later handed, context line included - a
        // vector of "İade yapılmaz" alone would put this chunk next to the one that says the opposite.
        _store.StateOfAsync("iade", Arg.Any<CancellationToken>()).Returns((KnowledgeDocumentState?)null);

        await _handler.Handle(new IndexKnowledgeDocumentCommand("iade", Markdown), CancellationToken.None);

        await _embedder.Received().EmbedAsync(
            Arg.Is<string>(text => text.StartsWith("İade Politikası > Müşterinin vazgeçmesi")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LeaveTheDocumentAlone_When_NeitherTextNorModelChanged()
    {
        _store.StateOfAsync("iade", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState(KnowledgeContentHash.Of(Markdown), "bge-m3", KnowledgeOrigin.Library));

        var result = await _handler.Handle(new IndexKnowledgeDocumentCommand("iade", Markdown), CancellationToken.None);

        result.Unchanged.Should().BeTrue();
        await _embedder.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().ReplaceDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<KnowledgeOrigin>(),
            Arg.Any<IReadOnlyList<EmbeddedKnowledgeChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_EmbedAgain_When_OnlyTheModelChanged()
    {
        _store.StateOfAsync("iade", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState(KnowledgeContentHash.Of(Markdown), "nomic-embed-text", KnowledgeOrigin.Library));

        var result = await _handler.Handle(new IndexKnowledgeDocumentCommand("iade", Markdown), CancellationToken.None);

        result.Unchanged.Should().BeFalse();
        await _store.Received(1).ReplaceDocumentAsync(
            "iade", Arg.Any<string>(), "bge-m3", KnowledgeOrigin.Library,
            Arg.Any<IReadOnlyList<EmbeddedKnowledgeChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_EmbedAgain_When_TheTextChanged()
    {
        _store.StateOfAsync("iade", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState(KnowledgeContentHash.Of("eski metin"), "bge-m3", KnowledgeOrigin.Library));

        var result = await _handler.Handle(new IndexKnowledgeDocumentCommand("iade", Markdown), CancellationToken.None);

        result.Unchanged.Should().BeFalse();
        await _store.Received(1).ReplaceDocumentAsync(
            "iade", KnowledgeContentHash.Of(Markdown), "bge-m3", KnowledgeOrigin.Library,
            Arg.Any<IReadOnlyList<EmbeddedKnowledgeChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RecordThatTheDocumentWasUploaded_When_ItCameThroughTheApi()
    {
        // Where a document came from is what the start-up clean-up goes by: it removes library documents
        // whose file has gone, and an uploaded one never had a file to begin with.
        _store.StateOfAsync("kampanya", Arg.Any<CancellationToken>()).Returns((KnowledgeDocumentState?)null);

        await _handler.Handle(
            new IndexKnowledgeDocumentCommand("kampanya", Markdown, KnowledgeOrigin.Uploaded),
            CancellationToken.None);

        await _store.Received(1).ReplaceDocumentAsync(
            "kampanya", Arg.Any<string>(), "bge-m3", KnowledgeOrigin.Uploaded,
            Arg.Any<IReadOnlyList<EmbeddedKnowledgeChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RefuseAnUpload_When_TheNameBelongsToALibraryDocument()
    {
        // The file would win on the next start and the upload would silently vanish; better to say so now.
        _store.StateOfAsync("iade", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState(KnowledgeContentHash.Of("eski metin"), "bge-m3", KnowledgeOrigin.Library));

        var upload = () => _handler.Handle(
            new IndexKnowledgeDocumentCommand("iade", Markdown, KnowledgeOrigin.Uploaded),
            CancellationToken.None);

        await upload.Should().ThrowAsync<ConflictException>();
        await _embedder.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().ReplaceDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<KnowledgeOrigin>(),
            Arg.Any<IReadOnlyList<EmbeddedKnowledgeChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_LetTheFileWin_AndSaySo_When_ALibraryFileTakesAnUploadedDocumentsName()
    {
        // The reverse of the refusal above, and it cannot be refused: nobody is waiting at the start-up to be
        // told. The file wins - it is the reviewed, committed copy - but the upload it replaced is reported
        // rather than overwritten without a trace.
        _store.StateOfAsync("kampanya", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState(KnowledgeContentHash.Of("yüklenen metin"), "bge-m3", KnowledgeOrigin.Uploaded));

        var result = await _handler.Handle(
            new IndexKnowledgeDocumentCommand("kampanya", Markdown, KnowledgeOrigin.Library),
            CancellationToken.None);

        result.ReplacedAnUpload.Should().BeTrue();
        await _store.Received(1).ReplaceDocumentAsync(
            "kampanya", KnowledgeContentHash.Of(Markdown), "bge-m3", KnowledgeOrigin.Library,
            Arg.Any<IReadOnlyList<EmbeddedKnowledgeChunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotReportAReplacedUpload_When_ALibraryDocumentIsSimplyUpdated()
    {
        _store.StateOfAsync("iade", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState(KnowledgeContentHash.Of("eski metin"), "bge-m3", KnowledgeOrigin.Library));

        var result = await _handler.Handle(new IndexKnowledgeDocumentCommand("iade", Markdown), CancellationToken.None);

        result.ReplacedAnUpload.Should().BeFalse();
    }

    /// <summary>A stand-in vector that differs per text, so a wrong embedding is visible in an assertion.</summary>
    private static float[] VectorFor(string text) => [text.Length, text[0]];
}
