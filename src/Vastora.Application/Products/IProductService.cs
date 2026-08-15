using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

public interface IProductService
{
    Task<ProductResponse> CreateAsync(string tenantId, string businessId, CreateProductRequest request, CancellationToken ct = default);

    /// <summary>BackOffice listing — every status included.</summary>
    Task<List<ProductResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default);

    /// <summary>Public Shop catalog — Active products only, optionally filtered.</summary>
    Task<List<ProductResponse>> GetPublicCatalogAsync(string businessId, string? categoryId, string? search, CancellationToken ct = default);

    Task<ProductResponse> GetByIdAsync(string businessId, string productId, CancellationToken ct = default);

    Task<ProductResponse> UpdateAsync(string tenantId, string businessId, string productId, UpdateProductRequest request, CancellationToken ct = default);

    Task<ProductResponse> UpdateStatusAsync(string tenantId, string businessId, string productId, ProductStatus status, CancellationToken ct = default);

    /// <summary>Appends an already-stored image's URL to Product.Images — see IFileStorageService for the storage step (§9.5).</summary>
    Task<ProductResponse> AddImageAsync(string tenantId, string businessId, string productId, string imageUrl, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string productId, CancellationToken ct = default);
}
