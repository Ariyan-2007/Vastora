using Vastora.Application.Common.Exceptions;
using Vastora.Application.Products;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Products;

public class ProductServiceTests
{
    private static (ProductService Service, FakeMongoRepository<Product> Products, FakeMongoRepository<TenantAccount> Tenants) Create()
    {
        var products = new FakeMongoRepository<Product>();
        var tenants = new FakeMongoRepository<TenantAccount>();
        return (new ProductService(products, tenants), products, tenants);
    }

    private static CreateProductRequest ValidRequest(string name = "Widget") =>
        new("cat-1", name, null, "SKU-1", "desc", 9.99m, null, 10, true, null, null, null);

    [Fact]
    public async Task CreateAsync_RejectsOnceBusinessHitsPlanProductLimit()
    {
        var (service, products, tenants) = Create();
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
        var (service, _, tenants) = Create();
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
        var (service, products, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        products.Seed(
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = "cat-1", Name = "Red Shoe", Status = ProductStatus.Active },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = "cat-2", Name = "Blue Shoe", Status = ProductStatus.Active },
            new Product { TenantId = tenant.Id, BusinessId = "biz-1", CategoryId = "cat-1", Name = "Red Hat", Status = ProductStatus.Draft });

        var byCategory = await service.GetPublicCatalogAsync("biz-1", "cat-1", null, CancellationToken.None);
        var bySearch = await service.GetPublicCatalogAsync("biz-1", null, "red", CancellationToken.None);
        var byBoth = await service.GetPublicCatalogAsync("biz-1", "cat-2", "shoe", CancellationToken.None);

        Assert.Single(byCategory); // "Red Hat" excluded: Draft, not Active
        Assert.Single(bySearch);   // case-insensitive match on "Red Shoe", Draft "Red Hat" excluded
        Assert.Single(byBoth);
    }

    [Fact]
    public async Task AddImageAsync_AppendsToImagesList()
    {
        var (service, products, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount())[0];
        var product = products.Seed(new Product { TenantId = tenant.Id, BusinessId = "biz-1" })[0];

        var result = await service.AddImageAsync(tenant.Id, "biz-1", product.Id, "/uploads/biz-1/abc.jpg", CancellationToken.None);

        Assert.Contains("/uploads/biz-1/abc.jpg", result.Images);
    }
}
