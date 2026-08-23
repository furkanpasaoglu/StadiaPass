using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

internal sealed class StadiaPassCatalogueClient(HttpClient httpClient) : IStadiaPassCatalogueClient
{
    public async Task<IReadOnlyList<VenueSummary>> GetVenuesAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(new Uri("/api/v1/venues", UriKind.Relative), cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IReadOnlyList<VenueSummary>>(cancellationToken) ?? []
            : [];
    }

    public async Task<ApiResult<VenueSummary>> CreateVenueAsync(
        VenueInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/venues", ToPayload(input), cancellationToken);

        return await ReadAsync<VenueSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<VenueSummary>> UpdateVenueAsync(
        VenueInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/v1/venues/{input.Id}", ToPayload(input), cancellationToken);

        return await ReadAsync<VenueSummary>(response, cancellationToken);
    }

    public async Task<ApiResult<bool>> DeleteVenueAsync(Guid venueId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            new Uri($"/api/v1/venues/{venueId}", UriKind.Relative), cancellationToken);

        return await ReadEmptyAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<CategorySummary>> GetCategoriesAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var route = activeOnly ? "/api/v1/categories?activeOnly=true" : "/api/v1/categories";
        var response = await httpClient.GetAsync(new Uri(route, UriKind.Relative), cancellationToken);

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<IReadOnlyList<CategorySummary>>(cancellationToken) ?? []
            : [];
    }

    public async Task<ApiResult<CategorySummary>> CreateCategoryAsync(
        CategoryInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/categories",
            new { name = input.Name, description = input.Description, allowedVenueKinds = input.AllowedVenueKinds },
            cancellationToken);

        return await ReadAsync<CategorySummary>(response, cancellationToken);
    }

    public async Task<ApiResult<CategorySummary>> UpdateCategoryAsync(
        CategoryInput input,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/v1/categories/{input.Id}",
            new
            {
                name = input.Name,
                description = input.Description,
                isActive = input.IsActive,
                allowedVenueKinds = input.AllowedVenueKinds
            },
            cancellationToken);

        return await ReadAsync<CategorySummary>(response, cancellationToken);
    }

    public async Task<ApiResult<bool>> DeleteCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            new Uri($"/api/v1/categories/{categoryId}", UriKind.Relative), cancellationToken);

        return await ReadEmptyAsync(response, cancellationToken);
    }

    private static object ToPayload(VenueInput input) => new
    {
        name = input.Name,
        city = input.City,
        kind = input.Kind,
        blocks = input.Blocks.Select(block => new
        {
            name = block.Name,
            rowCount = block.RowCount,
            seatsPerRow = block.SeatsPerRow,
            priceMultiplier = block.PriceMultiplier
        })
    };

    private static async Task<ApiResult<bool>> ReadEmptyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return ApiResult.Success(true);
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);

        return ApiResult.Failure<bool>(
            problem?.Detail ?? problem?.Title ?? $"The API responded with {(int)response.StatusCode}.");
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
        catch (Exception exception)
            when (exception is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
