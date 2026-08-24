using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

internal sealed class StadiaPassApiClient(HttpClient httpClient) : IStadiaPassApiClient
{
    public async Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var route = string.IsNullOrWhiteSpace(category)
            ? "/api/v1/matches"
            : $"/api/v1/matches?category={Uri.EscapeDataString(category)}";

        return await httpClient.GetFromJsonAsync<IReadOnlyList<MatchSummary>>(route, cancellationToken) ?? [];
    }

    public async Task<SeatMap?> GetSeatMapAsync(Guid matchId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            new Uri($"/api/v1/matches/{matchId}/seats", UriKind.Relative), cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<SeatMap>(cancellationToken)
            : null;
    }

    public async Task<ApiResult<SeatReservation>> ReserveSeatAsync(
        Guid matchId,
        string seatNumber,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync(
            new Uri($"/api/v1/matches/{matchId}/seats/{Uri.EscapeDataString(seatNumber)}/reservation", UriKind.Relative),
            content: null,
            cancellationToken);

        return await ReadAsync<SeatReservation>(response, cancellationToken);
    }

    /// <summary>
    /// The card is relayed to the API over the internal service-to-service call and nothing about it is
    /// kept on this side: the payload is built inline and goes out of scope with the request.
    /// </summary>
    public async Task<ApiResult<TicketSummary>> PurchaseAsync(
        Guid matchId,
        PurchaseInput input,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            matchId,
            seatNumber = input.SeatNumber,
            cardHolderName = input.CardHolderName,
            cardNumber = input.CardNumber,
            expirationMonth = input.ExpirationMonth,
            expirationYear = input.ExpirationYear,
            cvv = input.Cvv
        };

        var response = await httpClient.PostAsJsonAsync("/api/v1/tickets", payload, cancellationToken);

        return await ReadAsync<TicketSummary>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TicketSummary>> GetMyTicketsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<IReadOnlyList<TicketSummary>>("/api/v1/tickets/mine", cancellationToken) ?? [];

    public async Task<IReadOnlyList<VenueSummary>> GetVenuesAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(new Uri("/api/v1/venues", UriKind.Relative), cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IReadOnlyList<VenueSummary>>(cancellationToken) ?? []
            : [];
    }

    public async Task<ApiResult<MatchSummary>> CreateMatchAsync(
        CreateMatchInput input,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            categoryId = input.CategoryId,
            venueId = input.VenueId,
            homeTeam = input.HomeTeam,
            awayTeam = input.AwayTeam,
            kickOffUtc = new DateTimeOffset(input.KickOffLocal, TimeZoneInfo.Local.GetUtcOffset(input.KickOffLocal)),
            basePrice = input.BasePrice,
            currency = input.Currency
        };

        var response = await httpClient.PostAsJsonAsync("/api/v1/matches", payload, cancellationToken);

        return await ReadAsync<MatchSummary>(response, cancellationToken);
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

        if (response.StatusCode is HttpStatusCode.Forbidden)
        {
            return ApiResult.Failure<T>("Your account does not carry the permission required for this action.");
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);

        return ApiResult.Failure<T>(
            problem?.Detail ?? problem?.Title ?? $"The API responded with {(int)response.StatusCode}.",
            (problem as ValidationProblemDetails)?.Errors.ToDictionary(entry => entry.Key, entry => entry.Value));
    }

    private static async Task<ProblemDetails?> TryReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return response.StatusCode is HttpStatusCode.BadRequest
                ? await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken)
                : await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
