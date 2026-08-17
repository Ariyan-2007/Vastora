namespace Vastora.Application.Categories;

public interface ICategoryService
{
    Task<CategoryResponse> CreateAsync(string tenantId, string businessId, CreateCategoryRequest request, CancellationToken ct = default);

    Task<List<CategoryResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default);

    /// <summary>Same Categories, nested by ParentCategoryId instead of a flat list — §9.5.</summary>
    Task<List<CategoryTreeNode>> GetTreeAsync(string businessId, CancellationToken ct = default);

    Task<CategoryResponse> GetByIdAsync(string businessId, string categoryId, CancellationToken ct = default);

    Task<CategoryResponse> UpdateAsync(string tenantId, string businessId, string categoryId, UpdateCategoryRequest request, CancellationToken ct = default);

    /// <summary>Sets ImageUrl only, so an upload doesn't force the caller to resend the whole category (mirrors ProductService.AddImageAsync).</summary>
    Task<CategoryResponse> SetImageAsync(string tenantId, string businessId, string categoryId, string imageUrl, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string categoryId, CancellationToken ct = default);
}
