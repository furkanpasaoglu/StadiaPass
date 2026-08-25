using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StadiaPass.Application.Common.Abstractions;
using StadiaPass.Application.Common.Exceptions;
using StadiaPass.Application.Tickets.Commands.ReserveSeat;
using StadiaPass.Domain.Abstractions;
using StadiaPass.Domain.Common;
using StadiaPass.Domain.Matches;

namespace StadiaPass.Application.UnitTests.Tickets;

/// <summary>
/// The seat holder is taken from the signed-in caller, never from the request body. These tests pin that
/// down, along with the single save at the end of a successful reservation.
/// </summary>
public sealed class ReserveSeatCommandHandlerTests
{
    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    private readonly ReserveSeatCommandHandler _handler;

    public ReserveSeatCommandHandlerTests()
    {
        _currentUser.Reference.Returns(TestData.CurrentUserId);
        _currentUser.IsAuthenticated.Returns(true);
        _dateTimeProvider.UtcNow.Returns(TestData.Now);

        // A substituted transaction that never runs its body would let every assertion below pass against a
        // handler that saves nothing at all, so the fake actually executes what it is given.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));

        _handler = new ReserveSeatCommandHandler(
            _matchRepository, _unitOfWork, _currentUser, _dateTimeProvider);
    }

    [Fact]
    public async Task Should_ReserveTheSeatForTheSignedInCaller_When_TheSeatIsAvailable()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - the holder is the Keycloak subject from ICurrentUser, not anything the request supplied.
        var seat = match.Seats.Single(candidate => candidate.SeatNumber.ToString() == TestData.SeatNumber);
        seat.Status.Should().Be(SeatStatus.Reserved);
        seat.HolderReference.Should().Be(TestData.CurrentUserId);
        seat.HolderReference.Should().NotBe(TestData.OtherUserId);
    }

    [Fact]
    public async Task Should_PassTheClockToTheDomain_When_TheSeatIsReserved()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert - the hold window is measured from the injected clock, so the test is not racing wall time.
        result.ReservationExpiresAtUtc.Should().Be(TestData.Now + Match.ReservationWindow);
        _ = _dateTimeProvider.Received().UtcNow;
    }

    [Fact]
    public async Task Should_SaveExactlyOnce_When_TheReservationSucceeds()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnTheReservedSeatDetails_When_TheReservationSucceeds()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.MatchId.Should().Be(match.Id);
        result.SeatNumber.Should().Be(TestData.SeatNumber);
        result.Status.Should().Be(nameof(SeatStatus.Reserved));
        result.Price.Should().Be(100m);
        result.Currency.Should().Be("TRY");
    }

    [Fact]
    public async Task Should_LoadOnlyTheRequestedSeat_When_TheReservationIsHandled()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - the filtered load is what keeps a 20k seat venue from being pulled into memory.
        await _matchRepository.Received(1)
            .GetWithSeatAsync(match.Id, TestData.SeatNumber, Arg.Any<CancellationToken>());
        await _matchRepository.DidNotReceive().GetWithSeatMapAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ThrowNotFound_When_TheMatchDoesNotExist()
    {
        // Arrange
        var command = new ReserveSeatCommand(Guid.CreateVersion7(), TestData.SeatNumber);
        _matchRepository
            .GetWithSeatAsync(command.MatchId, command.SeatNumber, Arg.Any<CancellationToken>())
            .Returns((Match?)null);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotSave_When_TheSeatIsAlreadyHeldBySomebodyElse()
    {
        // Arrange - another customer got there first.
        var match = TestData.FootballMatch();
        match.ReserveSeat(TestData.SeatNumber, TestData.OtherUserId, TestData.Now);

        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert - the domain refuses and nothing is committed.
        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_NotSwallowTheFailure_When_SavingThrows()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("the database went away"));

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert - a write failure has to surface, not be reported as a successful reservation.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Should_ForwardTheCancellationToken_When_TheReservationIsHandled()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        using var cancellation = new CancellationTokenSource();

        // Act
        await _handler.Handle(command, cancellation.Token);

        // Assert
        await _matchRepository.Received(1)
            .GetWithSeatAsync(match.Id, TestData.SeatNumber, cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    [Fact]
    public async Task Should_HandTheCountersToTheDatabase_When_AFreeSeatIsReserved()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - written as a relative update rather than saved from memory. Two people holding two
        // different seats of the same match would otherwise both write the totals they read, and one of the
        // two holds would quietly vanish from the counts.
        await _matchRepository.Received(1)
            .ApplySeatReservationToCountersAsync(match, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_MoveNoCounters_When_AHoldThatHasRunOutIsTakenOver()
    {
        // Arrange - somebody else held this seat and never came back for it.
        var match = TestData.FootballMatch();
        match.ReserveSeat(TestData.SeatNumber, TestData.OtherUserId, TestData.Now);
        _dateTimeProvider.UtcNow.Returns(TestData.Now + Match.ReservationWindow + TimeSpan.FromMinutes(1));

        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - the seat only changes hands. It was counted as reserved before and still is, so moving
        // the counters would invent a seat that does not exist.
        await _matchRepository.Received(1)
            .ApplySeatReservationToCountersAsync(match, 0, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_TakeTheMatchRowBeforeTheSeat_When_TheReservationIsWritten()
    {
        // Arrange
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - coarsest row first, the same order a sale, a void and the sweeper use. Two transactions
        // reaching for the match and a seat in opposite orders is how deadlocks are made.
        Received.InOrder(() =>
        {
            _matchRepository.ApplySeatReservationToCountersAsync(
                match, Arg.Any<int>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Should_ReserveTheSeatOnlyOnce_When_TheTransactionIsRetried()
    {
        // Arrange - the retrying execution strategy runs the delegate again after a transient failure. The
        // rollback undoes what the database did and nothing else, so anything the handler put in the
        // delegate would be run a second time against a seat it had already moved.
        var match = TestData.FootballMatch();
        var command = new ReserveSeatCommand(match.Id, TestData.SeatNumber);
        GivenTheRepositoryReturns(match, command);

        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var operation = call.Arg<Func<CancellationToken, Task>>();

                await operation(call.Arg<CancellationToken>());
                await operation(call.Arg<CancellationToken>());
            });

        // Act
        var act = async () => await _handler.Handle(command, CancellationToken.None);

        // Assert - a second pass must not throw. The transition happened before the transition opened, so
        // all the retry repeats is database work, which the rollback already took back.
        await act.Should().NotThrowAsync();
        match.ReservedSeatCount.Should().Be(1);
    }

    private void GivenTheRepositoryReturns(Match match, ReserveSeatCommand command) =>
        _matchRepository
            .GetWithSeatAsync(command.MatchId, command.SeatNumber, Arg.Any<CancellationToken>())
            .Returns(match);
}
