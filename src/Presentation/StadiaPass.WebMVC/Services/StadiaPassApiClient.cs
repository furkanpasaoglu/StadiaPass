using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

internal sealed class StadiaPassApiClient(HttpClient httpClient) : IStadiaPassApiClient
{
    public async Task<IReadOnlyList<MatchSummary>> GetUpcomingMatchesAsync(
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<MatchSummary>>("/api/v1/matches", cancellationToken) ?? [];

    public async Task<IReadOnlyList<TicketSummary>> GetTicketsByMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<TicketSummary>>(
            $"/api/v1/tickets?matchId={matchId}", cancellationToken) ?? [];

    public async Task<ApiResult<TicketSummary>> CreateTicketAsync(
        CreateTicketInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/tickets", input, cancellationToken);

        return await ReadAsync<TicketSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<TicketSummary>> ReserveTicketAsync(
        Guid ticketId,
        string holderReference,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/v1/tickets/{ticketId}/reservation",
            new { holderReference },
            cancellationToken);

        return await ReadAsync<TicketSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<TicketSummary>> ConfirmSaleAsync(
        Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync(
            new Uri($"/api/v1/tickets/{ticketId}/sale", UriKind.Relative),
            content: null,
            cancellationToken);

        return await ReadAsync<TicketSummary>(response, cancellationToken);
    }

    private static async Task<ApiResult<T>> ReadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);

            return value is null
                ? ApiResult.Failure<T>("The API returned an empty response.")
                : ApiResult.Success(value);
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);

        return ApiResult.Failure<T>(
            problem?.Detail ?? problem?.Title ?? $"The API responded with {(int)response.StatusCode}.",
            (problem as ValidationProblemDetails)?.Errors.ToDictionary(
                entry => entry.Key,
                entry => entry.Value));
    }

    private static async Task<ProblemDetails?> TryReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return response.StatusCode is System.Net.HttpStatusCode.BadRequest
                ? await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken)
                : await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
