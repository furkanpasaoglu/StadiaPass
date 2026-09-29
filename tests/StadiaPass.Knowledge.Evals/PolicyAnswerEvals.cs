using System.Globalization;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace StadiaPass.Knowledge.Evals;

/// <summary>
/// Scores the policy assistant end to end, on three things that can each go wrong on their own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retrieval:</b> the section the answer has to come from is among the passages the assistant read.
/// If it is not, nothing downstream can save the answer, and the fix is in the documents or the embedding,
/// not the prompt.
/// </para>
/// <para>
/// <b>Grounding:</b> the answer cites that section by its number. A right answer with the wrong citation is
/// an answer the model wrote from somewhere else - memory, or a neighbouring passage - and got lucky.
/// </para>
/// <para>
/// <b>Refusal:</b> for a question the documents do not cover, the assistant says so in the agreed words
/// and does not improvise. There is always a nearest passage, so this is the model's judgement being
/// scored, and the case next door to a real rule is the one that tells honest from lucky.
/// </para>
/// </remarks>
public sealed partial class PolicyAnswerEvals(PolicyAssistantFixture assistant, ITestOutputHelper output)
    : IClassFixture<PolicyAssistantFixture>
{
    /// <summary>The exact sentence the instructions ask for; also what the host answers when nothing was retrieved.</summary>
    private const string NoInformation = "belgelerde bilgi yok";

    private static readonly TimeSpan CaseTimeout = TimeSpan.FromMinutes(3);

    [SkippableTheory]
    [MemberData(nameof(GoldenPolicyQuestions.CaseIds), MemberType = typeof(GoldenPolicyQuestions))]
    public async Task Assistant_answers_from_the_right_section_or_says_the_documents_do_not_cover_it(string caseId)
    {
        Skip.If(assistant.Client is null, assistant.SkipReason);

        var evalCase = GoldenPolicyQuestions.Cases[caseId];

        using var timeout = new CancellationTokenSource(CaseTimeout);

        var answer = await assistant.AskAsync(evalCase.Question, timeout.Token);

        output.WriteLine($"Q: {evalCase.Question}");
        output.WriteLine($"A: {answer.Answer}");
        output.WriteLine(string.Join(
            Environment.NewLine,
            answer.Sources.Select(source =>
                $"   [{source.Number}] {source.Document} > {source.Heading} ({source.Score.ToString("F2", CultureInfo.InvariantCulture)})")));

        var citations = Citations(answer.Answer);

        if (!evalCase.DocumentsCoverIt)
        {
            answer.Answer.Should().ContainEquivalentOf(
                NoInformation,
                "the documents do not cover this question, and an answer to it can only have been made up");

            return;
        }

        if (evalCase.RequiredAll is { Count: > 0 })
        {
            // A message with several rule questions: every one of them has its own section, and an answer
            // that covers some of them and calls the rest "not in the documents" is the failure being measured.
            foreach (var required in evalCase.RequiredAll)
            {
                var source = answer.Sources.FirstOrDefault(candidate =>
                    candidate.Document == required.Document && candidate.Heading == required.Heading);

                source.Should().NotBeNull(
                    $"'{required.Document} > {required.Heading}' answers one of the questions asked and has to "
                    + "be among the passages the assistant reads");

                citations.Should().Contain(
                    source!.Number,
                    $"the answer has to cite [{source.Number}] ({required.Heading}) for the question it answers");
            }

            return;
        }

        var expected = evalCase.Expected!;
        var wanted = string.Join(" | ", expected.Select(section => $"{section.Document} > {section.Heading}"));

        var retrieved = answer.Sources
            .Where(candidate => expected.Any(section =>
                candidate.Document == section.Document && candidate.Heading == section.Heading))
            .ToList();

        retrieved.Should().NotBeEmpty(
            $"retrieval must bring back one of '{wanted}' among the passages the assistant reads; nothing "
            + "after retrieval can answer from a section it was never shown");

        answer.Answer.Should().NotContainEquivalentOf(
            NoInformation,
            "a section that answers this question was retrieved, so refusing it is the model not reading");

        citations.Should().Contain(
            number => retrieved.Any(source => source.Number == number),
            $"the answer must be written from one of {string.Join(", ", retrieved.Select(source => $"[{source.Number}]"))} "
            + $"({wanted}), and citing only other passages means it was written from somewhere else");
    }

    private static HashSet<int> Citations(string answer) =>
        CitationPattern().Matches(answer)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToHashSet();

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex CitationPattern();
}
