using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

public interface IStadiaPassApiClient
{
    Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(string? category = null, CancellationToken cancellationToken = default);

    Task<SeatMap?> GetSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<ApiResult<SeatReservation>> ReserveSeatAsync(Guid matchId, string seatNumber, CancellationToken cancellationToken = default);

    Task<ApiResult<TicketSummary>> PurchaseAsync(
        Guid matchId,
        PurchaseInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketSummary>> GetMyTicketsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VenueSummary>> GetVenuesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<MatchSummary>> CreateMatchAsync(CreateMatchInput input, CancellationToken cancellationToken = default);
}
