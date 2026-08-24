using StadiaPass.Domain.Matches;
using StadiaPass.Domain.Tickets;

namespace StadiaPass.Domain.UnitTests.Tickets;

/// <summary>
/// The access code is what a turnstile trusts. It used to be the first twelve characters of a version 7
/// GUID, which is the issue timestamp in hexadecimal: two tickets bought in the same millisecond collided,
/// and anyone who knew roughly when a ticket was bought could compute it. These tests pin down that the code
/// is neither derived from the clock nor repeatable.
/// </summary>
public sealed class TicketAccessCodeTests
{
    private const string SafeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    [Fact]
    public void Should_IssueDistinctAccessCodes_When_ManyTicketsShareTheSameInstant()
    {
        // Arrange - one sold seat, one frozen clock: every ticket below is issued at the very same moment.
        var (match, seat) = SoldSeat();

        // Act
        var codes = Enumerable
            .Range(0, 500)
            .Select(_ => Ticket.IssueFor(match, seat, TestData.Now).AccessCode)
            .ToArray();

        // Assert - a code derived from the issue time would collapse to a single value here.
        codes.Distinct(StringComparer.Ordinal).Should().HaveCount(codes.Length);
    }

    [Fact]
    public void Should_DrawTheAccessCodeFromTheUnambiguousAlphabet_When_ATicketIsIssued()
    {
        // Arrange
        var (match, seat) = SoldSeat();

        // Act
        var accessCode = Ticket.IssueFor(match, seat, TestData.Now).AccessCode;

        // Assert - no character a person confuses at a turnstile, and enough of them to be unguessable.
        accessCode.Should().HaveLength(12);
        accessCode.Should().MatchRegex($"^[{SafeAlphabet}]+$");
    }

    private static (Match Match, MatchSeat Seat) SoldSeat()
    {
        var match = TestData.FootballMatch();

        match.ReserveSeat("MARATON-1-1", TestData.Holder, TestData.Now);

        return (match, match.ConfirmSeatSale("MARATON-1-1", TestData.Holder, TestData.Now));
    }
}
