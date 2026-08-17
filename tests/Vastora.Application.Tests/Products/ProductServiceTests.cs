using Vastora.Application.Common.Exceptions;
using Vastora.Application.Products;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Products;

public class ProductServiceTests
{
    private static (ProductService Service, FakeMongoRepository<Product> Products, FakeMongoRepository<TenantAccount> Tenants, FakeMongoRepository<Category> Categories) Create()
    {
        var products = new FakeMongoRepository<Product>();
        var tenants = new FakeMongoRepository<TenantAccount>();
        var categories = new FakeMongoRepository<Category>();
        return (new ProductService(products, tenants, categories), products, tenants, categories);
    }

    private static CreateProductRequest ValidRequest(string name = "Widget") =>
        new("cat-1", name, null, "SKU-1", "desc", 9.99m, null, 10, true, null, null, null);

    [Fact]
    public async Task CreateAsync_RejectsOnceBusinessHitsPlanProductLimit()
    {
        var (service, products, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Trial })[0]; // limit: 20 products/business
        for (var i = 0; i < 20; i++)
        {
            products.Seed(new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = $"P{i}", Slug = $"p{i}" });
        }

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(tenant.Id, "biz-1", ValidRequest("21st"), CancellationToken.None));

        Assert.Contains("Trial", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_GeneratesIdsForNewVariants()
    {
        var (service, _, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        var request = ValidRequest() with
        {
            Variants = [new ProductVariantRequest(null, "Red / L", "SKU-1-RED-L", null, 5)]
        };

        var result = await service.CreateAsync(tenant.Id, "biz-1", request, CancellationToken.None);

        var variant = Assert.Single(result.Variants);
        Assert.False(string.IsNullOrEmpty(variant.Id));
        Assert.Equal("Red / L", variant.AttributeSummary);
    }

    [Fact]
    public async Task GetPublicCatalogAsync_FiltersByCategoryAndSearch_AndExcludesNonActive()
    {
        var (service, products, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        products.Seed(
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = "cat-1", Name = "Red Shoe", Status = ProductStatus.Active },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = "cat-2", Name = "Blue Shoe", Status = ProductStatus.Active },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = "cat-1", Name = "Red Hat", Status = ProductStatus.Draft });

        var byCategory = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(CategoryId: "cat-1"), CancellationToken.None);
        var bySearch = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(Search: "red"), CancellationToken.None);
        var byBoth = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(CategoryId: "cat-2", Search: "shoe"), CancellationToken.None);

        Assert.Single(byCategory.Items); // "Red Hat" excluded: Draft, not Active
        Assert.Single(bySearch.Items);   // case-insensitive match on "Red Shoe", Draft "Red Hat" excluded
        Assert.Single(byBoth.Items);
    }

    [Fact]
    public async Task GetPublicCatalogAsync_WithholdsAnActiveProductUntilItsPublishWindowOpens()
    {
        var (service, products, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        products.Seed(
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "Live now", Status = ProductStatus.Active },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "Goes live Friday", Status = ProductStatus.Active, PublishedAt = DateTime.UtcNow.AddDays(3) },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "Retired", Status = ProductStatus.Active, UnpublishedAt = DateTime.UtcNow.AddDays(-1) });

        var result = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(), CancellationToken.None);

        // §9.28: Status alone was never enough to express "goes live Friday".
        Assert.Single(result.Items);
        Assert.Equal("Live now", result.Items[0].Name);
    }

    [Fact]
    public async Task GetPublicCatalogAsync_StripsCostPriceFromThePublicProjection()
    {
        var (service, products, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        var seeded = products.Seed(new Product
        {
            TenantId = tenant.Id, BusinessId = "biz-1", Name = "Widget",
            Status = ProductStatus.Active, Price = 50m, CostPrice = 20m
        })[0];

        var publicResult = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(), CancellationToken.None);
        var backOfficeResult = await service.GetByIdAsync("biz-1", seeded.Id, CancellationToken.None);

        // What a business pays its suppliers is not something its customers get to read.
        Assert.Null(publicResult.Items[0].CostPrice);
        Assert.Null(publicResult.Items[0].UnitMargin);
        Assert.Equal(20m, backOfficeResult.CostPrice);
        Assert.Equal(30m, backOfficeResult.UnitMargin);
    }

    [Fact]
    public async Task GetPublicCatalogAsync_SortsByPriceAndByRelevance()
    {
        var (service, products, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        products.Seed(
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "Cheap", Status = ProductStatus.Active, Price = 5m, SortWeight = 9 },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "Pricey", Status = ProductStatus.Active, Price = 90m, SortWeight = 5 },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "Promoted", Status = ProductStatus.Active, Price = 40m, IsFeatured = true, SortWeight = 8 });

        var byPrice = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(Sort: ProductSort.PriceAscending), CancellationToken.None);
        var byRelevance = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(Sort: ProductSort.Relevance), CancellationToken.None);

        Assert.Equal("Cheap", byPrice.Items[0].Name);
        // Relevance is the merchandising default: what the seller promoted comes first, whatever it costs.
        Assert.Equal("Promoted", byRelevance.Items[0].Name);
    }

    [Fact]
    public async Task GetCatalogFacetsAsync_CountsBrandsAndPriceRangeOverTheSameFilterAsTheListing()
    {
        var (service, products, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        products.Seed(
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "A", Status = ProductStatus.Active, Price = 10m, Brand = "Acme", Tags = ["sale"] },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "B", Status = ProductStatus.Active, Price = 60m, Brand = "Acme" },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", Name = "C", Status = ProductStatus.Active, Price = 30m, Brand = "Other" });

        var facets = await service.GetCatalogFacetsAsync("biz-1", new CatalogQuery(), CancellationToken.None);

        Assert.Equal(3, facets.TotalCount);
        Assert.Contains(facets.Brands, b => b.Value == "Acme" && b.Count == 2);
        Assert.Equal(10m, facets.MinPrice);
        Assert.Equal(60m, facets.MaxPrice);
        Assert.Contains(facets.Tags, t => t.Value == "sale" && t.Count == 1);
    }

    [Fact]
    public async Task GetPublicCatalogAsync_FilteringByAParentCategory_IncludesSubcategoryProducts()
    {
        var (service, products, tenants, categories) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        var electronics = categories.Seed(new Category { BusinessId = "biz-1", Name = "Electronics", ParentCategoryId = null })[^1];
        var phones = categories.Seed(new Category { BusinessId = "biz-1", Name = "Phones", ParentCategoryId = electronics.Id })[^1];
        categories.Seed(new Category { BusinessId = "biz-1", Name = "Clothing", ParentCategoryId = null });
        products.Seed(
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = electronics.Id, Name = "TV", Status = ProductStatus.Active },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = phones.Id, Name = "Handset", Status = ProductStatus.Active },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = "cat-clothing", Name = "Shirt", Status = ProductStatus.Active });

        var byParent = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(CategoryId: electronics.Id), CancellationToken.None);
        var bySubcategory = await service.GetPublicCatalogAsync("biz-1", new CatalogQuery(CategoryId: phones.Id), CancellationToken.None);

        Assert.Equal(2, byParent.Items.Count); // both "TV" (direct) and "Handset" (under Phones)
        Assert.Contains(byParent.Items, p => p.Name == "TV");
        Assert.Contains(byParent.Items, p => p.Name == "Handset");
        Assert.Single(bySubcategory.Items); // picking the subcategory itself narrows back down
        Assert.Equal("Handset", bySubcategory.Items[0].Name);
    }

    [Fact]
    public async Task AddImageAsync_AppendsToImagesList()
    {
        var (service, products, tenants, _) = Create();
        var tenant = tenants.Seed(new TenantAccount())[0];
        var product = products.Seed(new Product { TenantId = tenant.Id, BusinessId = "biz-1" })[0];

        var result = await service.AddImageAsync(tenant.Id, "biz-1", product.Id, "/uploads/biz-1/abc.jpg", CancellationToken.None);

        Assert.Contains("/uploads/biz-1/abc.jpg", result.Images);
    }
}
