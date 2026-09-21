using StadiaPass.Application.Knowledge;

namespace StadiaPass.Application.UnitTests.Knowledge;

/// <summary>
/// A chunk is what the model gets to read, so what matters is that every section arrives whole, in order,
/// and carrying enough of its surroundings to be understood on its own - a paragraph that says "the refund
/// goes to the card" is useless without knowing it is the paragraph about cancelled matches.
/// </summary>
public sealed class KnowledgeChunkerTests
{
    private const string Policy = """
        # İade Politikası

        ## Maç iptal edildiğinde

        Bilet bedeli karta iade edilir.

        Bankaya göre birkaç gün sürebilir.

        ## Müşterinin vazgeçmesi

        İade yapılmaz.
        """;

    [Fact]
    public void Chunk_MakesOneChunkPerSection_InDocumentOrder()
    {
        var chunks = KnowledgeChunker.Chunk("iade-politikasi", Policy);

        chunks.Select(chunk => chunk.Heading).Should().Equal("Maç iptal edildiğinde", "Müşterinin vazgeçmesi");
        chunks.Select(chunk => chunk.Position).Should().Equal(0, 1);
    }

    [Fact]
    public void Chunk_KeepsEveryParagraphOfASectionTogether()
    {
        var chunks = KnowledgeChunker.Chunk("iade-politikasi", Policy);

        chunks[0].Text.Should().Contain("Bilet bedeli karta iade edilir.")
            .And.Contain("Bankaya göre birkaç gün sürebilir.");
        chunks[1].Text.Should().NotContain("Bankaya göre");
    }

    [Fact]
    public void Chunk_PrefixesEachChunkWithTheDocumentTitleAndSectionHeading()
    {
        // The line the embedding sees first. Without it the second chunk is the two words "İade yapılmaz",
        // which is as close to "iade" as the paragraph that says the opposite.
        var chunks = KnowledgeChunker.Chunk("iade-politikasi", Policy);

        chunks[1].Text.Should().StartWith("İade Politikası > Müşterinin vazgeçmesi");
        chunks[1].Document.Should().Be("iade-politikasi");
        chunks[1].Title.Should().Be("İade Politikası");
    }

    [Fact]
    public void Chunk_KeepsAListInsideItsSection()
    {
        const string rules = """
            # Giriş Kuralları

            ## Yasak eşyalar

            Alınmaz:

            - Cam şişe
            - Şemsiye

            ## Kayıp eşya

            Gişeye başvurulur.
            """;

        var chunks = KnowledgeChunker.Chunk("giris", rules);

        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Contain("- Cam şişe").And.Contain("- Şemsiye");
    }

    [Fact]
    public void Chunk_TurnsTextBeforeTheFirstHeadingIntoItsOwnChunk()
    {
        const string withIntro = """
            # Başlık

            Giriş paragrafı.

            ## Bölüm

            Gövde.
            """;

        var chunks = KnowledgeChunker.Chunk("doc", withIntro);

        chunks.Should().HaveCount(2);
        chunks[0].Heading.Should().BeEmpty();
        chunks[0].Text.Should().StartWith("Başlık").And.Contain("Giriş paragrafı.");
    }

    [Fact]
    public void Chunk_FallsBackToTheDocumentNameWhenThereIsNoTitle()
    {
        var chunks = KnowledgeChunker.Chunk("notlar", "## Tek bölüm\n\nMetin.");

        chunks.Single().Title.Should().Be("notlar");
        chunks.Single().Text.Should().StartWith("notlar > Tek bölüm");
    }

    [Fact]
    public void Chunk_ReturnsNothingForAnEmptyDocument()
    {
        KnowledgeChunker.Chunk("bos", "   \n\n").Should().BeEmpty();
    }
}
