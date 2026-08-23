using StadiaPass.WebMVC.Models;

namespace StadiaPass.WebMVC.Services;

/// <summary>Back-office catalogue calls: venues and sport categories, brokered by the API.</summary>
public interface IStadiaPassCatalogueClient
{
    Task<IReadOnlyList<VenueSummary>> GetVenuesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<VenueSummary>> CreateVenueAsync(VenueInput input, CancellationToken cancellationToken = default);

    Task<ApiResult<VenueSummary>> UpdateVenueAsync(VenueInput input, CancellationToken cancellationToken = default);

    Task<ApiResult<bool>> DeleteVenueAsync(Guid venueId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategorySummary>> GetCategoriesAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default);

    Task<ApiResult<CategorySummary>> CreateCategoryAsync(
        CategoryInput input,
        CancellationToken cancellationToken = default);

    Task<ApiResult<CategorySummary>> UpdateCategoryAsync(
        CategoryInput input,
        CancellationToken cancellationToken = default);

    Task<ApiResult<bool>> DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
