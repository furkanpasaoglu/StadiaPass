using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

public interface IStadiaPassApiClient
{
    Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(string? category = null, CancellationToken cancellationToken = default);

    Task<MatchSearchResult> SearchMatchesAsync(string term, CancellationToken cancellationToken = default);

    Task<SeatMap?> GetSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<ApiResult<SeatReservation>> ReserveSeatAsync(Guid matchId, string seatNumber, CancellationToken cancellationToken = default);

    Task<ApiResult<TicketSummary>> PurchaseAsync(
        Guid matchId,
        PurchaseInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MyTicket>> GetMyTicketsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VenueSummary>> GetVenuesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<MatchSummary>> CreateMatchAsync(CreateMatchInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls a match off. Selling stops at once; the refunds follow on their own.
    /// </summary>
    Task<ApiResult<bool>> CancelMatchAsync(
        Guid matchId,
        string reason,
        CancellationToken cancellationToken = default);
}
