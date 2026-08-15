using Vastora.Application.Categories;
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
}
