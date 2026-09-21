using NSubstitute;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Knowledge;
using StadiaPass.Application.Knowledge.Commands.RemoveKnowledgeDocument;

namespace StadiaPass.Application.UnitTests.Knowledge;

/// <summary>
/// Removing through the API is for what came through the API. A library document is owned by its file:
/// taking its chunks out would last until the next start, when the loader finds the file and puts them
/// back, so the honest answer to that request is "no, delete the file" rather than a success that undoes itself.
/// </summary>
public sealed class RemoveKnowledgeDocumentCommandHandlerTests
{
    private readonly IKnowledgeStore _store = Substitute.For<IKnowledgeStore>();

    private readonly RemoveKnowledgeDocumentCommandHandler _handler;

    public RemoveKnowledgeDocumentCommandHandlerTests() =>
        _handler = new RemoveKnowledgeDocumentCommandHandler(_store);

    [Fact]
    public async Task Should_RemoveTheDocument_When_ItWasUploaded()
    {
        _store.StateOfAsync("kampanya", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState("hash", "bge-m3", KnowledgeOrigin.Uploaded));

        await _handler.Handle(new RemoveKnowledgeDocumentCommand("kampanya"), CancellationToken.None);

        await _store.Received(1).RemoveDocumentAsync("kampanya", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_SayNotFound_When_TheStoreHoldsNoSuchDocument()
    {
        _store.StateOfAsync("yok", Arg.Any<CancellationToken>()).Returns((KnowledgeDocumentState?)null);

        var removal = () => _handler.Handle(new RemoveKnowledgeDocumentCommand("yok"), CancellationToken.None);

        await removal.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_RefuseToRemoveALibraryDocument()
    {
        _store.StateOfAsync("iade-politikasi", Arg.Any<CancellationToken>())
            .Returns(new KnowledgeDocumentState("hash", "bge-m3", KnowledgeOrigin.Library));

        var removal = () => _handler.Handle(new RemoveKnowledgeDocumentCommand("iade-politikasi"), CancellationToken.None);

        await removal.Should().ThrowAsync<ConflictException>();
        await _store.DidNotReceive().RemoveDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
