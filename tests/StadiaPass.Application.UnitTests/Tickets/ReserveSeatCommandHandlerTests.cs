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

    private void GivenTheRepositoryReturns(Match match, ReserveSeatCommand command) =>
        _matchRepository
            .GetWithSeatAsync(command.MatchId, command.SeatNumber, Arg.Any<CancellationToken>())
            .Returns(match);
}
