using System.Net;
using System.Net.Http.Json;

namespace StadiaPass.McpServer.Api;

internal sealed class CatalogueApiClient(HttpClient httpClient) : ICatalogueApiClient
{
    public async Task<IReadOnlyList<MatchSummary>> GetUpcomingMatchesAsync(
        string? category,
        CancellationToken cancellationToken = default)
    {
        var route = string.IsNullOrWhiteSpace(category)
            ? "/api/v1/matches"
            : $"/api/v1/matches?category={Uri.EscapeDataString(category)}";

        return await httpClient.GetFromJsonAsync<IReadOnlyList<MatchSummary>>(
            new Uri(route, UriKind.Relative), cancellationToken) ?? [];
    }

    public async Task<MatchSearchResult?> SearchMatchesAsync(
        string term,
        CancellationToken cancellationToken = default)
    {
        var route = $"/api/v1/matches/search?q={Uri.EscapeDataString(term)}";

        return await httpClient.GetFromJsonAsync<MatchSearchResult>(
            new Uri(route, UriKind.Relative), cancellationToken);
    }

    public async Task<SeatMap?> GetSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            new Uri($"/api/v1/matches/{matchId}/seats", UriKind.Relative), cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SeatMap>(cancellationToken);
    }

    public async Task<MatchRevenue?> GetMatchRevenueAsync(
        Guid matchId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            new Uri($"/api/v1/matches/{matchId}/revenue", UriKind.Relative), cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MatchRevenue>(cancellationToken);
    }

    public async Task<PolicySearchResult?> SearchPoliciesAsync(
        string question,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var route = $"/api/v1/knowledge/search?q={Uri.EscapeDataString(question)}&limit={limit}";

        return await httpClient.GetFromJsonAsync<PolicySearchResult>(
            new Uri(route, UriKind.Relative), cancellationToken);
    }
}
