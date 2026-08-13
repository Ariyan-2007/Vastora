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
    string Description,
    string ImageUrl,
    int SortOrder,
    bool IsActive);
