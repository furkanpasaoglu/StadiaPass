using StadiaPass.AgentHost.Guardrails;

namespace StadiaPass.AgentHost.UnitTests.Guardrails;

/// <summary>
/// What counts as a personal datum, and - just as importantly - what does not. A redactor that is too
/// eager is not a safer redactor: it eats the figures the analyst exists to report, and the first time it
/// turns a price into a placeholder somebody switches it off, which is how a guardrail stops guarding.
/// </summary>
/// <remarks>
/// Every detector here validates rather than pattern-matches wherever the datum lets it - Luhn for a card,
/// the national identifier's own checksum for itself - so that sixteen digits are only a card number when
/// they can actually be one. The negative tests are the point of the file.
/// </remarks>
public sealed class PersonalDataTests
{
    /// <summary>
    /// Passes the identifier's checksum and belongs to nobody: the nine digits it is built from are a one
    /// and eight zeroes, and the last two are whatever the checksum then forces them to be.
    /// </summary>
    private const string ValidNationalId = "10000000078";

    [Fact]
    public void Should_RemoveAnEmailAddress_When_ItIsPastedIntoAQuestion()
    {
        // Arrange
        const string question = "ahmet.yilmaz@example.com hangi maca bilet aldi?";

        // Act
        var redaction = PersonalData.Redact(question);

        // Assert
        redaction.Text.Should().Be("[redacted email address] hangi maca bilet aldi?");
        redaction.Removed.Should().Equal(PersonalDataKind.EmailAddress);
    }

    /// <summary>
    /// The same number in the six shapes people write it in. It is deliberately a 500 prefix, which no
    /// Turkish operator has ever been allocated: test data reaches logs, screenshots and pull requests, so
    /// it must not be reachable by anybody.
    /// </summary>
    [Theory]
    [InlineData("+90 500 000 00 00")]
    [InlineData("+905000000000")]
    [InlineData("0500 000 00 00")]
    [InlineData("05000000000")]
    [InlineData("500 000 00 00")]
    [InlineData("0500-000-00-00")]
    public void Should_RemoveAMobileNumber_When_ItIsWrittenAnyOfTheUsualWays(string number)
    {
        // Arrange
        var question = $"Musteri {number} numarasindan aradi.";

        // Act
        var redaction = PersonalData.Redact(question);

        // Assert
        redaction.Text.Should().Be("Musteri [redacted phone number] numarasindan aradi.");
        redaction.Removed.Should().Equal(PersonalDataKind.PhoneNumber);
    }

    [Theory]
    [InlineData("4242424242424242")]
    [InlineData("4242 4242 4242 4242")]
    [InlineData("4000-0000-0000-9995")]
    [InlineData("378282246310005")]
    public void Should_RemoveACardNumber_When_TheDigitsPassLuhn(string card)
    {
        // Arrange
        var question = $"Odeme {card} kartiyla yapildi.";

        // Act
        var redaction = PersonalData.Redact(question);

        // Assert
        redaction.Text.Should().Be("Odeme [redacted card number] kartiyla yapildi.");
        redaction.Removed.Should().Equal(PersonalDataKind.PaymentCard);
    }

    [Fact]
    public void Should_LeaveSixteenDigitsAlone_When_TheyDoNotPassLuhn()
    {
        // Arrange - one digit off a real test card, which is what tells a checksum from a guess.
        const string text = "Referans 4242424242424243 numarali kayit.";

        // Act
        var redaction = PersonalData.Redact(text);

        // Assert
        redaction.Text.Should().Be(text);
        redaction.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Should_RemoveANationalIdentifier_When_TheChecksumHolds()
    {
        // Arrange
        var text = $"TCKN {ValidNationalId} ile kayitli.";

        // Act
        var redaction = PersonalData.Redact(text);

        // Assert
        redaction.Text.Should().Be("TCKN [redacted national id] ile kayitli.");
        redaction.Removed.Should().Equal(PersonalDataKind.NationalId);
    }

    [Fact]
    public void Should_LeaveElevenDigitsAlone_When_TheChecksumFails()
    {
        // Arrange
        const string text = "Siparis 12345678901 numarali.";

        // Act
        var redaction = PersonalData.Redact(text);

        // Assert
        redaction.Text.Should().Be(text);
        redaction.Removed.Should().BeEmpty();
    }

    /// <summary>
    /// The figures this agent exists to report, in the shapes the tools actually return them. Every one of
    /// these is a number in a text a guardrail is about to read, and not one of them is anybody's business
    /// but the box office's.
    /// </summary>
    [Theory]
    [InlineData("Mac id d2b1f0a4-5c73-4e9a-9f21-0b6c8e5a1d33 icin.")]
    [InlineData("{\"asOfUtc\":\"2026-09-07T18:22:41.1234567+00:00\",\"result\":{\"netRevenue\":1750.00}}")]
    [InlineData("Kapasite 742, satilan 500, doluluk 67.4 yuzde.")]
    [InlineData("Koltuk GUNEY-4-7, bilet kodu 46ZEYJ2XQR9K, fiyat 500.00 TRY.")]
    [InlineData("Kick-off 2026-09-07 20:45, 12 blok, 33000 kisilik stadyum.")]
    public void Should_ChangeNothing_When_TheTextIsOrdinaryBoxOfficeData(string text)
    {
        // Act
        var redaction = PersonalData.Redact(text);

        // Assert
        redaction.Text.Should().Be(text);
        redaction.Removed.Should().BeEmpty();
    }

    [Fact]
    public void Should_ReportEveryKindItRemoved_When_AQuestionCarriesMoreThanOne()
    {
        // Arrange
        const string question = "ahmet@example.com, 0500 000 00 00, kart 4242 4242 4242 4242.";

        // Act
        var redaction = PersonalData.Redact(question);

        // Assert
        redaction.Text.Should().Be(
            "[redacted email address], [redacted phone number], kart [redacted card number].");
        redaction.Removed.Should().BeEquivalentTo(
        [
            PersonalDataKind.EmailAddress,
            PersonalDataKind.PhoneNumber,
            PersonalDataKind.PaymentCard
        ]);
    }

    [Fact]
    public void Should_ReportTheKindOnlyOnce_When_TheSameKindAppearsTwice()
    {
        // Arrange
        const string question = "ahmet@example.com ve mehmet@example.com ayni maci soruyor.";

        // Act
        var redaction = PersonalData.Redact(question);

        // Assert - the log line says what leaked out of the room, not how many times.
        redaction.Removed.Should().Equal(PersonalDataKind.EmailAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Should_ChangeNothing_When_ThereIsNoTextAtAll(string? text)
    {
        // Act
        var redaction = PersonalData.Redact(text);

        // Assert
        redaction.Text.Should().Be(text ?? string.Empty);
        redaction.Removed.Should().BeEmpty();
    }
}
