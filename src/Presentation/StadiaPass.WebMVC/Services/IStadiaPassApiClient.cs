using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

public interface IStadiaPassApiClient
{
    Task<IReadOnlyList<MatchSummary>> GetUpcomingMatchesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketSummary>> GetTicketsByMatchAsync(Guid matchId, CancellationToken cancellationToken = default);

    Task<ApiResult<TicketSummary>> CreateTicketAsync(CreateTicketInput input, CancellationToken cancellationToken = default);

    Task<ApiResult<TicketSummary>> ReserveTicketAsync(Guid ticketId, string holderReference, CancellationToken cancellationToken = default);

    Task<ApiResult<TicketSummary>> ConfirmSaleAsync(Guid ticketId, CancellationToken cancellationToken = default);
}
