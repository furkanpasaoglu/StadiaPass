using StadiaPass.Application.Knowledge;
using StadiaPass.Application.Knowledge.Commands.IndexKnowledgeDocument;

namespace StadiaPass.Application.UnitTests.Knowledge;

/// <summary>
/// The name ends up in a URL and in every citation the assistant shows, and the text ends up in front of
/// a model, one call per section. Both are checked before any of that happens.
/// </summary>
public sealed class IndexKnowledgeDocumentCommandValidatorTests
{
    private const string Markdown = "# Kampanya\n\n## Erken alım\n\nErken alımda indirim uygulanmaz.";

    private readonly IndexKnowledgeDocumentCommandValidator _validator = new();

    [Theory]
    [InlineData("iade-politikasi")]
    [InlineData("kampanya-2026")]
    public void Should_Accept_ANameOfLowercaseLettersDigitsAndHyphens(string name) =>
        _validator.Validate(new IndexKnowledgeDocumentCommand(name, Markdown, KnowledgeOrigin.Uploaded))
            .IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("İade Politikası")]
    [InlineData("iade_politikasi")]
    [InlineData("-iade")]
    [InlineData("../secrets")]
    public void Should_Reject_ANameThatIsNotASlug(string name) =>
        _validator.Validate(new IndexKnowledgeDocumentCommand(name, Markdown, KnowledgeOrigin.Uploaded))
            .IsValid.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# Yalnız başlık")]
    public void Should_Reject_ADocumentWithNothingToIndex(string markdown) =>
        _validator.Validate(new IndexKnowledgeDocumentCommand("kampanya", markdown, KnowledgeOrigin.Uploaded))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Should_Reject_ADocumentThatIsTooLong() =>
        _validator.Validate(new IndexKnowledgeDocumentCommand(
                "kampanya",
                "## Bölüm\n\n" + new string('a', IndexKnowledgeDocumentCommandValidator.MaxMarkdownLength),
                KnowledgeOrigin.Uploaded))
            .IsValid.Should().BeFalse();
}
