using Vastora.Application.Categories;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;

namespace Vastora.Application.Tests.Categories;

public class CategoryServiceTests
{
    [Fact]
    public async Task GetTreeAsync_NestsChildrenUnderParent_AndSortsBySortOrder()
    {
        var categories = new FakeMongoRepository<Category>();
        var service = new CategoryService(categories);

        var root = categories.Seed(new Category { BusinessId = "biz-1", Name = "Electronics", ParentCategoryId = null, SortOrder = 1 })[0];
        categories.Seed(
            new Category { BusinessId = "biz-1", Name = "Phones", ParentCategoryId = root.Id, SortOrder = 2 },
            new Category { BusinessId = "biz-1", Name = "Laptops", ParentCategoryId = root.Id, SortOrder = 1 },
            new Category { BusinessId = "biz-1", Name = "Clothing", ParentCategoryId = null, SortOrder = 2 });

        var tree = await service.GetTreeAsync("biz-1", CancellationToken.None);

        Assert.Equal(2, tree.Count); // two top-level categories
        var electronics = Assert.Single(tree, c => c.Name == "Electronics");
        Assert.Equal(2, electronics.Children.Count);
        Assert.Equal("Laptops", electronics.Children[0].Name); // SortOrder 1 before Phones' SortOrder 2
        Assert.Empty(electronics.Children[0].Children);
    }

    [Fact]
    public async Task UpdateAsync_MovesCategoryUnderANewParent()
    {
        var categories = new FakeMongoRepository<Category>();
        var service = new CategoryService(categories);

        var electronics = categories.Seed(new Category { TenantId = "tenant-1", BusinessId = "biz-1", Name = "Electronics", ParentCategoryId = null })[^1];
        var clothing = categories.Seed(new Category { TenantId = "tenant-1", BusinessId = "biz-1", Name = "Clothing", ParentCategoryId = null })[^1];
        var phones = categories.Seed(new Category { TenantId = "tenant-1", BusinessId = "biz-1", Name = "Phones", ParentCategoryId = electronics.Id })[^1];

        var result = await service.UpdateAsync(
            "tenant-1", "biz-1", phones.Id,
            new UpdateCategoryRequest("Phones", clothing.Id, "", "", 0, true),
            CancellationToken.None);

        Assert.Equal(clothing.Id, result.ParentCategoryId);
    }

    [Fact]
    public async Task UpdateAsync_RejectsMovingACategoryUnderItsOwnDescendant()
    {
        var categories = new FakeMongoRepository<Category>();
        var service = new CategoryService(categories);

        var electronics = categories.Seed(new Category { TenantId = "tenant-1", BusinessId = "biz-1", Name = "Electronics", ParentCategoryId = null })[^1];
        var phones = categories.Seed(new Category { TenantId = "tenant-1", BusinessId = "biz-1", Name = "Phones", ParentCategoryId = electronics.Id })[^1];

        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdateAsync(
            "tenant-1", "biz-1", electronics.Id,
            new UpdateCategoryRequest("Electronics", phones.Id, "", "", 0, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsACategoryAsItsOwnParent()
    {
        var categories = new FakeMongoRepository<Category>();
        var service = new CategoryService(categories);

        var electronics = categories.Seed(new Category { TenantId = "tenant-1", BusinessId = "biz-1", Name = "Electronics", ParentCategoryId = null })[^1];

        await Assert.ThrowsAsync<ValidationAppException>(() => service.UpdateAsync(
            "tenant-1", "biz-1", electronics.Id,
            new UpdateCategoryRequest("Electronics", electronics.Id, "", "", 0, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task SetImageAsync_SetsOnlyTheImage_LeavingOtherFieldsUntouched()
    {
        var categories = new FakeMongoRepository<Category>();
        var service = new CategoryService(categories);
        var electronics = categories.Seed(new Category { TenantId = "tenant-1", BusinessId = "biz-1", Name = "Electronics", SortOrder = 3 })[^1];

        var result = await service.SetImageAsync("tenant-1", "biz-1", electronics.Id, "/uploads/biz-1/electronics.jpg", CancellationToken.None);

        Assert.Equal("/uploads/biz-1/electronics.jpg", result.ImageUrl);
        Assert.Equal("Electronics", result.Name);
        Assert.Equal(3, result.SortOrder);
    }
}
