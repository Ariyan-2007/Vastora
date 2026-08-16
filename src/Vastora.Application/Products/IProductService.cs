using Vastora.Application.Common;
using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

public interface IProductService
{
    Task<ProductResponse> CreateAsync(string tenantId, string businessId, CreateProductRequest request, CancellationToken ct = default);

    /// <summary>BackOffice listing — every status included, paged (§9.18).</summary>
    Task<PagedResult<ProductResponse>> GetForBusinessAsync(string businessId, PageRequest page, string? search, CancellationToken ct = default);

    /// <summary>
    /// Public Shop catalog — Active and inside its publish window, filtered, sorted and paged
    /// (§9.28/§9.29). Cost price is stripped from this projection.
    /// </summary>
    Task<PagedResult<ProductResponse>> GetPublicCatalogAsync(string businessId, CatalogQuery query, CancellationToken ct = default);

    /// <summary>§9.29. Selectable filter values and their counts, computed over the same filter as the listing.</summary>
    Task<CatalogFacetsResponse> GetCatalogFacetsAsync(string businessId, CatalogQuery query, CancellationToken ct = default);

    Task<ProductResponse> GetByIdAsync(string businessId, string productId, CancellationToken ct = default);

    Task<ProductResponse> UpdateAsync(string tenantId, string businessId, string productId, UpdateProductRequest request, CancellationToken ct = default);

    Task<ProductResponse> UpdateStatusAsync(string tenantId, string businessId, string productId, ProductStatus status, CancellationToken ct = default);

    /// <summary>Appends an already-stored image's URL to Product.Images — see IFileStorageService for the storage step (§9.5).</summary>
    Task<ProductResponse> AddImageAsync(string tenantId, string businessId, string productId, string imageUrl, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string productId, CancellationToken ct = default);

    /// <summary>
    /// §9.28. Bulk upsert keyed on SKU. Onboarding a business with 2,000 SKUs was 2,000 API calls
    /// before this — and the Growth plan's 2,000-product cap is reachable by exactly the kind of
    /// tenant who will not enter them by hand.
    /// </summary>
    Task<ProductImportResult> ImportAsync(
        string tenantId, string businessId, IReadOnlyList<ProductImportRow> rows,
        IReadOnlyDictionary<string, string> categoryIdsByName, CancellationToken ct = default);

    Task<List<ProductImportRow>> ExportAsync(
        string businessId, IReadOnlyDictionary<string, string> categoryNamesById, CancellationToken ct = default);
}
