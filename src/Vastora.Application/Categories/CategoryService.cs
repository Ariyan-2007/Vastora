using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.Categories;

public class CategoryService(IMongoRepository<Category> categories) : ICategoryService
{
    public async Task<CategoryResponse> CreateAsync(string tenantId, string businessId, CreateCategoryRequest request, CancellationToken ct = default)
    {
        var slug = await GenerateUniqueSlugAsync(businessId, request.Slug ?? request.Name, ct);

        var category = new Category
        {
            TenantId = tenantId,
            BusinessId = businessId,
            Name = request.Name,
            Slug = slug,
            ParentCategoryId = request.ParentCategoryId,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            SortOrder = request.SortOrder,
            IsActive = true
        };

        await categories.AddAsync(category, ct);
        return Map(category);
    }

    public async Task<List<CategoryResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default)
    {
        var list = await categories.FindAsync(c => c.BusinessId == businessId, ct);
        return list.OrderBy(c => c.SortOrder).Select(Map).ToList();
    }

    public async Task<CategoryResponse> GetByIdAsync(string businessId, string categoryId, CancellationToken ct = default)
    {
        var category = await GetScopedAsync(businessId, categoryId, ct);
        return Map(category);
    }

    public async Task<CategoryResponse> UpdateAsync(string tenantId, string businessId, string categoryId, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var category = await GetScopedAsync(businessId, categoryId, ct);
        if (category.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Category), categoryId);
        }

        category.Name = request.Name;
        category.Description = request.Description;
        category.ImageUrl = request.ImageUrl;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        await categories.UpdateAsync(category, ct);
        return Map(category);
    }

    public async Task DeleteAsync(string tenantId, string businessId, string categoryId, CancellationToken ct = default)
    {
        var category = await GetScopedAsync(businessId, categoryId, ct);
        if (category.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Category), categoryId);
        }

        await categories.DeleteAsync(categoryId, ct);
    }

    public async Task<List<CategoryTreeNode>> GetTreeAsync(string businessId, CancellationToken ct = default)
    {
        var flat = await categories.FindAsync(c => c.BusinessId == businessId, ct);
        var byParent = flat.OrderBy(c => c.SortOrder).ToLookup(c => c.ParentCategoryId);

        List<CategoryTreeNode> Build(string? parentId) =>
            byParent[parentId]
                .Select(c => new CategoryTreeNode(
                    c.Id, c.BusinessId, c.Name, c.Slug, c.Description, c.ImageUrl, c.SortOrder, c.IsActive, Build(c.Id)))
                .ToList();

        return Build(null);
    }

    private async Task<Category> GetScopedAsync(string businessId, string categoryId, CancellationToken ct)
    {
        var category = await categories.GetByIdAsync(categoryId, ct);
        if (category is null || category.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Category), categoryId);
        }

        return category;
    }

    private async Task<string> GenerateUniqueSlugAsync(string businessId, string seed, CancellationToken ct)
    {
        var baseSlug = SlugHelper.Slugify(seed);
        var attempt = 0;
        while (true)
        {
            var candidate = SlugHelper.WithSuffix(baseSlug, attempt);
            var taken = await categories.ExistsAsync(c => c.BusinessId == businessId && c.Slug == candidate, ct);
            if (!taken)
            {
                return candidate;
            }

            attempt++;
        }
    }

    private static CategoryResponse Map(Category c) => new(
        c.Id, c.BusinessId, c.Name, c.Slug, c.ParentCategoryId, c.Description, c.ImageUrl, c.SortOrder, c.IsActive);
}
