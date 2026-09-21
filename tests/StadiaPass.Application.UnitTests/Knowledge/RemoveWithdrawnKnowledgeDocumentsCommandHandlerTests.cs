using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StadiaPass.Application.Knowledge;
using StadiaPass.Application.Knowledge.Commands.RemoveWithdrawnKnowledgeDocuments;

namespace StadiaPass.Application.UnitTests.Knowledge;

/// <summary>
/// A policy that has been withdrawn has to stop being answered from. Loading only ever adds and replaces,
/// so without this a deleted file's sections stay in the store and the assistant goes on quoting a rule
/// that no longer exists. The other half is what must NOT happen: an empty list is far more likely to be a
/// deployment that lost its documents than a decision to withdraw every policy at once.
/// </summary>
public sealed class RemoveWithdrawnKnowledgeDocumentsCommandHandlerTests
{
    private readonly IKnowledgeStore _store = Substitute.For<IKnowledgeStore>();

    private readonly RemoveWithdrawnKnowledgeDocumentsCommandHandler _handler;

    public RemoveWithdrawnKnowledgeDocumentsCommandHandlerTests()
    {
        _handler = new RemoveWithdrawnKnowledgeDocumentsCommandHandler(
            _store,
            NullLogger<RemoveWithdrawnKnowledgeDocumentsCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_RemoveWhatIsNoLongerInTheLibrary_AndSayWhatItRemoved()
    {
        string[] current = ["iade-politikasi", "stadyum-giris-kurallari"];
        _store.RemoveDocumentsNotInAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(["eski-kampanya-kurallari"]);

        var result = await _handler.Handle(new RemoveWithdrawnKnowledgeDocumentsCommand(current), CancellationToken.None);

        result.Removed.Should().Equal("eski-kampanya-kurallari");
        await _store.Received(1).RemoveDocumentsNotInAsync(
            Arg.Is<IReadOnlyCollection<string>>(kept => kept.SequenceEqual(current)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_RemoveNothing_When_TheLibraryIsEmpty()
    {
        var result = await _handler.Handle(new RemoveWithdrawnKnowledgeDocumentsCommand([]), CancellationToken.None);

        result.Removed.Should().BeEmpty();
        await _store.DidNotReceive().RemoveDocumentsNotInAsync(
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<CancellationToken>());
    }
}
