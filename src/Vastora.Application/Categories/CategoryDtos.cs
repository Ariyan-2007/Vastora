namespace Vastora.Application.Categories;

public record CategoryResponse(
    string Id,
    string BusinessId,
    string Name,
    string Slug,
    string? ParentCategoryId,
    string Description,
    string ImageUrl,
    int SortOrder,
    bool IsActive);

public record CreateCategoryRequest(
    string Name,
    string? Slug,
    string? ParentCategoryId,
    string Description,
    string ImageUrl,
    int SortOrder);

public record UpdateCategoryRequest(
    string Name,
    string? ParentCategoryId,
    string Description,
    string ImageUrl,
    int SortOrder,
    bool IsActive);

/// <summary>A Category nested under its children — built from the same flat collection (§9.5), no separate table.</summary>
public record CategoryTreeNode(
    string Id,
    string BusinessId,
    string Name,
    string Slug,
    string Description,
    string ImageUrl,
    int SortOrder,
    bool IsActive,
    List<CategoryTreeNode> Children);
