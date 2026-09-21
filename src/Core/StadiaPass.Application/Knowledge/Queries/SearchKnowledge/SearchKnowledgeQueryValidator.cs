using FluentValidation;

namespace StadiaPass.Application.Knowledge.Queries.SearchKnowledge;

/// <summary>
/// A question is a sentence or two; the ceiling is there so a caller cannot hand the embedding model a
/// page. The limit is capped where an answer stops being written from the chunks and starts being written
/// from a pile of them.
/// </summary>
internal sealed class SearchKnowledgeQueryValidator : AbstractValidator<SearchKnowledgeQuery>
{
    private const int MaxQuestionLength = 500;

    private const int MaxLimit = 10;

    public SearchKnowledgeQueryValidator()
    {
        RuleFor(query => query.Question)
            .NotEmpty().WithMessage("A question is required.")
            .MaximumLength(MaxQuestionLength)
            .WithMessage($"A question cannot be longer than {MaxQuestionLength} characters.");

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, MaxLimit)
            .WithMessage($"Between 1 and {MaxLimit} chunks can be asked for.");
    }
}
