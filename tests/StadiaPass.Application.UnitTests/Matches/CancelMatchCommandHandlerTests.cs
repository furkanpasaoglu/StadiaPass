using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Infrastructure.Abstractions;
using StadiaPass.Application.Matches.Commands.CancelMatch;
using StadiaPass.Application.Matches.Events;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.UnitTests.Matches;

/// <summary>
/// The synchronous half of calling a fixture off: shut the till, hand back the seats people are holding, and
/// say so out loud where the refunds can hear it. What was already paid for is settled ticket by ticket
/// afterwards, so nothing here touches a sold seat.
/// </summary>
public sealed class CancelMatchCommandHandlerTests
{
    private const string Reason = "the pitch is frozen";

    private const string OtherHolder = "9c1f2b44-0f4a-4e6b-8a7e-11c8d5b6e3aa";

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();

    private readonly IOutbox _outbox = Substitute.For<IOutbox>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    /// <summary>The counter update the repository hands back, so the test can see when it is run.</summary>
    private readonly Func<CancellationToken, Task> _writeCounters =
        Substitute.For<Func<CancellationToken, Task>>();

    private readonly CancelMatchCommandHandler _handler;

    private readonly Match _match = TestData.FootballMatch();

    public CancelMatchCommandHandlerTests()
    {
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        // A substituted transaction that never runs its body would let every assertion below pass against a
        // handler that saves nothing at all, so the fake actually executes what it is given.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));

        _writeCounters(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _matchRepository
            .PrepareMatchCancellationCounters(Arg.Any<Match>(), Arg.Any<int>())
            .Returns(_writeCounters);

        _matchRepository
            .GetWithHeldSeatsAsync(_match.Id, Arg.Any<CancellationToken>())
            .Returns(_match);

        _handler = new CancelMatchCommandHandler(
            _matchRepository,
            _cacheService,
            _outbox,
            _unitOfWork,
            _dateTimeProvider,
            NullLogger<CancelMatchCommandHandler>.Instance);
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheFixtureIsNotThere()
    {
        // Arrange
        _matchRepository
            .GetWithHeldSeatsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Match?)null);

        // Act
        var cancelling = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert
        await cancelling.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Should_GiveBackTheSeatsPeopleWereHolding()
    {
        // Arrange - two people mid-checkout when the fixture is called off.
        _match.ReserveSeat("MARATON-1-1", TestData.CurrentUserId, TestData.Now);
        _match.ReserveSeat("MARATON-1-2", OtherHolder, TestData.Now);

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - a hold left behind is a seat counted against a fixture nobody can buy from, and the
        // sweeper would not come past for another ten minutes.
        TestData.SeatOf(_match, "MARATON-1-1").Status.Should().Be(SeatStatus.Available);
        TestData.SeatOf(_match, "MARATON-1-2").Status.Should().Be(SeatStatus.Available);
    }

    [Fact]
    public async Task Should_TellTheCounterUpdateHowManySeatsCameBack()
    {
        // Arrange
        _match.ReserveSeat("MARATON-1-1", TestData.CurrentUserId, TestData.Now);
        _match.ReserveSeat("MARATON-1-2", OtherHolder, TestData.Now);

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - counted before the aggregate is asked to do anything, because afterwards the answer is
        // always zero and the row would be left claiming two seats are still held.
        _matchRepository.Received(1).PrepareMatchCancellationCounters(_match, 2);
    }

    [Fact]
    public async Task Should_LeaveSoldSeatsToTheSettlement()
    {
        // Arrange - one seat sold, one held.
        _match.ReserveSeat("MARATON-1-1", TestData.CurrentUserId, TestData.Now);
        _match.ConfirmSeatSale("MARATON-1-1", TestData.CurrentUserId, TestData.Now);
        _match.ReserveSeat("MARATON-1-2", OtherHolder, TestData.Now);

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the sold seat owes somebody money and is voided by its own ticket's settlement. Freeing it
        // here would lose the only record of what still has to be paid back, and the counter update would
        // move a seat that nothing had released.
        TestData.SeatOf(_match, "MARATON-1-1").Status.Should().Be(SeatStatus.Sold);
        _matchRepository.Received(1).PrepareMatchCancellationCounters(_match, 1);
    }

    [Fact]
    public async Task Should_StageTheAnnouncementBeforeTheSave()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the announcement and the cancellation have to be written by the same SaveChanges.
        // Publishing after the commit leaves a gap where a fixture is called off and nothing downstream is
        // ever told, which here means tickets nobody refunds.
        Received.InOrder(() =>
        {
            _outbox.Enqueue(Arg.Any<MatchCancelledEvent>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_NameTheFixtureAndWhyItWasCalledOff()
    {
        // Arrange
        // Two messages are staged, so only the one this test is about is kept.
        MatchCancelledEvent? announced = null;
        _outbox.Enqueue(Arg.Do<object>(message =>
        {
            if (message is MatchCancelledEvent cancellation)
            {
                announced = cancellation;
            }
        }));

        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the reason travels with every refund this sets off and ends up on the customer's
        // statement, so an announcement that loses it costs the explanation as well as the money.
        announced.Should().NotBeNull();
        announced!.MatchId.Should().Be(_match.Id);
        announced.Reason.Should().Be(Reason);
    }

    [Fact]
    public async Task Should_TellTheSearchIndexTheCatalogueChanged()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the projection this wakes is the one that takes a cancelled fixture out of the index. Say
        // nothing and the search box goes on offering a match nobody is playing, and its link goes on opening
        // a seat map, until somebody rebuilds the whole index by hand.
        _outbox.Received(1).Enqueue(Arg.Is<MatchCatalogueChangedEvent>(message => message.MatchId == _match.Id));
    }

    [Fact]
    public async Task Should_TakeTheMatchRowLast()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the match row is the coarsest lock in the system, wanted by every sale of the fixture, so
        // it is taken immediately before the commit rather than at the top where every one of those sales
        // would queue behind this cancellation's seat and outbox writes.
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _writeCounters(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_DropTheUpcomingListingFromTheCache()
    {
        // Act
        await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - the listing is cached for fifteen seconds and filters cancelled fixtures out, so until
        // this runs the front page keeps offering a match that is no longer being played.
        await _cacheService.Received(1).RemoveAsync("matches:upcoming", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheFixtureHasAlreadyBeenCalledOff()
    {
        // Arrange
        _match.Cancel(TestData.Now);

        // Act
        var cancelling = async () => await _handler.Handle(ACommand(), CancellationToken.None);

        // Assert - a second cancellation would announce a second round of refunds for tickets the first round
        // has already paid back.
        await cancelling.Should().ThrowAsync<DomainRuleViolationException>();
        _outbox.DidNotReceive().Enqueue(Arg.Any<object>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private CancelMatchCommand ACommand() => new(_match.Id, Reason);
}
