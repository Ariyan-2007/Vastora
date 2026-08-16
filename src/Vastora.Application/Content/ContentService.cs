using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Content;

public record ContentBlockRequest(
    ContentBlockType Type,
    string Slug,
    string Title,
    string Subtitle,
    string Body,
    string ImageUrl,
    string LinkUrl,
    string LinkLabel,
    string? ParentId,
    int SortOrder,
    bool IsPublished,
    DateTime? StartsAt,
    DateTime? EndsAt,
    string MetaTitle,
    string MetaDescription);

public record ContentBlockResponse(
    string Id,
    ContentBlockType Type,
    string Slug,
    string Title,
    string Subtitle,
    string Body,
    string ImageUrl,
    string LinkUrl,
    string LinkLabel,
    string? ParentId,
    int SortOrder,
    bool IsPublished,
    DateTime? StartsAt,
    DateTime? EndsAt,
    string MetaTitle,
    string MetaDescription,
    bool IsVisibleNow);

/// <summary>A nav menu, nested one level via ParentId.</summary>
public record MenuNode(string Id, string Title, string LinkUrl, int SortOrder, List<MenuNode> Children);

/// <summary>§9.30. The storefront content model that previously stopped at logo, banner and theme colour.</summary>
public interface IContentService
{
    Task<PagedResult<ContentBlockResponse>> GetAllAsync(string businessId, ContentBlockType? type, PageRequest page, CancellationToken ct = default);

    /// <summary>Public read — visible blocks only, honouring the publish schedule.</summary>
    Task<List<ContentBlockResponse>> GetPublicAsync(string businessId, ContentBlockType type, CancellationToken ct = default);

    Task<ContentBlockResponse> GetPublicBySlugAsync(string businessId, string slug, CancellationToken ct = default);

    Task<List<MenuNode>> GetMenuAsync(string businessId, CancellationToken ct = default);

    Task<ContentBlockResponse> CreateAsync(string tenantId, string businessId, ContentBlockRequest request, CancellationToken ct = default);

    Task<ContentBlockResponse> UpdateAsync(string tenantId, string businessId, string blockId, ContentBlockRequest request, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string blockId, CancellationToken ct = default);
}

/// <inheritdoc cref="IContentService"/>
public class ContentService(IMongoRepository<ContentBlock> blocks) : IContentService
{
    public async Task<PagedResult<ContentBlockResponse>> GetAllAsync(string businessId, ContentBlockType? type, PageRequest page, CancellationToken ct = default)
    {
        var result = await blocks.FindPagedAsync(
            b => b.BusinessId == businessId && (type == null || b.Type == type),
            page, b => b.SortOrder, SortDirection.Ascending, ct);

        return result.Map(Map);
    }

    public async Task<List<ContentBlockResponse>> GetPublicAsync(string businessId, ContentBlockType type, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var all = await blocks.FindAsync(b => b.BusinessId == businessId && b.Type == type && b.IsPublished, ct);

        // The schedule window is applied here rather than in the predicate: expressing
        // "null OR <= now" for two nullable dates in a Mongo predicate is far less readable than
        // the entity's own IsVisibleNow, and a business's content set is small by nature.
        return [.. all.Where(b => b.IsVisibleNow(now)).OrderBy(b => b.SortOrder).Select(Map)];
    }

    public async Task<ContentBlockResponse> GetPublicBySlugAsync(string businessId, string slug, CancellationToken ct = default)
    {
        var block = await blocks.FindOneAsync(b => b.BusinessId == businessId && b.Slug == slug, ct);
        if (block is null || !block.IsVisibleNow(DateTime.UtcNow))
        {
            throw new NotFoundException(nameof(ContentBlock), slug);
        }

        return Map(block);
    }

    public async Task<List<MenuNode>> GetMenuAsync(string businessId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var items = (await blocks.FindAsync(
                b => b.BusinessId == businessId && b.Type == ContentBlockType.MenuItem && b.IsPublished, ct))
            .Where(b => b.IsVisibleNow(now))
            .OrderBy(b => b.SortOrder)
            .ToList();

        // Same grouping approach as CategoryService.GetTreeAsync (§9.5) — one flat read, nested
        // in memory, no join.
        var byParent = items.Where(i => i.ParentId is not null).ToLookup(i => i.ParentId!);

        return [.. items
            .Where(i => i.ParentId is null)
            .Select(i => new MenuNode(i.Id, i.Title, i.LinkUrl, i.SortOrder,
                [.. byParent[i.Id].Select(c => new MenuNode(c.Id, c.Title, c.LinkUrl, c.SortOrder, []))]))];
    }

    public async Task<ContentBlockResponse> CreateAsync(string tenantId, string businessId, ContentBlockRequest request, CancellationToken ct = default)
    {
        await EnsureSlugIsFreeAsync(businessId, request.Slug, null, ct);

        var block = new ContentBlock { TenantId = tenantId, BusinessId = businessId };
        Apply(block, request);

        await blocks.AddAsync(block, ct);
        return Map(block);
    }

    public async Task<ContentBlockResponse> UpdateAsync(string tenantId, string businessId, string blockId, ContentBlockRequest request, CancellationToken ct = default)
    {
        var block = await GetScopedAsync(tenantId, businessId, blockId, ct);
        await EnsureSlugIsFreeAsync(businessId, request.Slug, blockId, ct);

        Apply(block, request);
        await blocks.UpdateAsync(block, ct);
        return Map(block);
    }

    public async Task DeleteAsync(string tenantId, string businessId, string blockId, CancellationToken ct = default)
    {
        await GetScopedAsync(tenantId, businessId, blockId, ct);
        await blocks.DeleteAsync(blockId, ct: ct);
    }

    private async Task EnsureSlugIsFreeAsync(string businessId, string slug, string? excludingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        var existing = await blocks.FindOneAsync(b => b.BusinessId == businessId && b.Slug == slug, ct);
        if (existing is not null && existing.Id != excludingId)
        {
            throw new ConflictException($"A content block with slug '{slug}' already exists.");
        }
    }

    private static void Apply(ContentBlock block, ContentBlockRequest request)
    {
        block.Type = request.Type;
        block.Slug = request.Slug;
        block.Title = request.Title;
        block.Subtitle = request.Subtitle;
        block.Body = request.Body;
        block.ImageUrl = request.ImageUrl;
        block.LinkUrl = request.LinkUrl;
        block.LinkLabel = request.LinkLabel;
        block.ParentId = request.ParentId;
        block.SortOrder = request.SortOrder;
        block.IsPublished = request.IsPublished;
        block.StartsAt = request.StartsAt;
        block.EndsAt = request.EndsAt;
        block.MetaTitle = request.MetaTitle;
        block.MetaDescription = request.MetaDescription;
    }

    private async Task<ContentBlock> GetScopedAsync(string tenantId, string businessId, string blockId, CancellationToken ct)
    {
        var block = await blocks.GetByIdAsync(blockId, ct);
        if (block is null || block.TenantId != tenantId || block.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(ContentBlock), blockId);
        }

        return block;
    }

    private static ContentBlockResponse Map(ContentBlock b) => new(
        b.Id, b.Type, b.Slug, b.Title, b.Subtitle, b.Body, b.ImageUrl, b.LinkUrl, b.LinkLabel,
        b.ParentId, b.SortOrder, b.IsPublished, b.StartsAt, b.EndsAt, b.MetaTitle, b.MetaDescription,
        b.IsVisibleNow(DateTime.UtcNow));
}
