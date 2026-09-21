using StadiaPass.AgentHost.Policy;

namespace StadiaPass.AgentHost.UnitTests.Policy;

/// <summary>
/// The prompt is the whole contract between retrieval and the model: the passages have to arrive whole,
/// numbered so an answer can point back at one, labelled with where they came from, and with the
/// question after them rather than before - a model reads top to bottom and answers what it read last.
/// </summary>
public sealed class PolicyPromptTests
{
    private static readonly PolicyPassage Refund = new(
        "iade-politikasi", "İade Politikası", "Maç iptal edildiğinde",
        "İade Politikası > Maç iptal edildiğinde\n\nBilet bedeli karta iade edilir.", 0.73);

    private static readonly PolicyPassage Hold = new(
        "rezervasyon-ve-satis-kosullari", "Rezervasyon ve Satış Koşulları", "Koltuk tutma süresi",
        "Rezervasyon ve Satış Koşulları > Koltuk tutma süresi\n\n10 dakika boyunca tutulur.", 0.61);

    [Fact]
    public void Build_NumbersEachPassageAndNamesItsSource()
    {
        var prompt = PolicyPrompt.Build("Maç iptal olursa?", [Refund, Hold]);

        prompt.Should().Contain("[1] İade Politikası > Maç iptal edildiğinde")
            .And.Contain("[2] Rezervasyon ve Satış Koşulları > Koltuk tutma süresi");
    }

    [Fact]
    public void Build_CarriesEachPassageWhole()
    {
        var prompt = PolicyPrompt.Build("Maç iptal olursa?", [Refund, Hold]);

        prompt.Should().Contain("Bilet bedeli karta iade edilir.")
            .And.Contain("10 dakika boyunca tutulur.");
    }

    [Fact]
    public void Build_PutsTheQuestionAfterThePassages()
    {
        var prompt = PolicyPrompt.Build("Maç iptal olursa?", [Refund]);

        prompt.IndexOf("Maç iptal olursa?", StringComparison.Ordinal)
            .Should().BeGreaterThan(prompt.IndexOf("Bilet bedeli", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_DoesNotRepeatTheContextLineThePassageAlreadyStartsWith()
    {
        // Every chunk starts with "Belge > Başlık" for the embedding's sake; the prompt labels the passage
        // with the same line, so the body is taken from after it or the model reads every heading twice.
        var prompt = PolicyPrompt.Build("?", [Refund]);

        prompt.Split("İade Politikası > Maç iptal edildiğinde").Should().HaveCount(2);
    }
}
