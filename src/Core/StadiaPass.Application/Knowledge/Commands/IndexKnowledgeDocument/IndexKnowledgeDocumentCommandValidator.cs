using FluentValidation;

namespace StadiaPass.Application.Knowledge.Commands.IndexKnowledgeDocument;

internal sealed class IndexKnowledgeDocumentCommandValidator : AbstractValidator<IndexKnowledgeDocumentCommand>
{
    public const int MaxNameLength = 80;

    /// <summary>
    /// Roughly forty pages. Every section costs a model call, so an upload with no ceiling is a way to keep
    /// the embedding model busy for as long as somebody likes.
    /// </summary>
    public const int MaxMarkdownLength = 100_000;

    public IndexKnowledgeDocumentCommandValidator()
    {
        // Lowercase letters, digits and single hyphens between them. The name travels in a URL and shows up
        // in every citation, so it is kept to what needs no escaping in either - which also leaves no room
        // for a path.
        RuleFor(command => command.Document)
            .NotEmpty().WithMessage("A document name is required.")
            .MaximumLength(MaxNameLength)
            .WithMessage($"A document name cannot be longer than {MaxNameLength} characters.")
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("A document name is lowercase letters and digits, separated by single hyphens.");

        RuleFor(command => command.Markdown)
            .NotEmpty().WithMessage("The document has no text.")
            .MaximumLength(MaxMarkdownLength)
            .WithMessage($"A document cannot be longer than {MaxMarkdownLength} characters.")
            .Must(markdown => KnowledgeChunker.Chunk("document", markdown ?? string.Empty).Count > 0)
            .WithMessage("The document has nothing to index: it needs text under its title or under a '## ' heading.");
    }
}
