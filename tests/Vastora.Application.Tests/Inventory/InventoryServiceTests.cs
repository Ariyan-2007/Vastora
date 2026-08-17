using Vastora.Application.Common.Exceptions;
using Vastora.Application.Inventory;
using Vastora.Application.Products;
using Vastora.Application.Tenants;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Inventory;

public class InventoryServiceTests
{
    private static (InventoryService Service, FakeMongoRepository<Product> Products, FakeMongoRepository<StockMovement> Movements) Create()
    {
        var products = new FakeMongoRepository<Product>();
        var movements = new FakeMongoRepository<StockMovement>();
        var tenants = new FakeMongoRepository<TenantAccount>();
        var productService = new ProductService(products, tenants, new FakeMongoRepository<Category>());
        var stockStore = new FakeProductStockStore(products);
        return (new InventoryService(products, movements, stockStore, productService), products, movements);
    }

    [Fact]
    public async Task RecordMovementAsync_UpdatesStockAndLogsMovement()
    {
        var (service, products, movements) = Create();
        var product = products.Seed(new Product { BusinessId = "biz-1", StockQuantity = 10 })[0];

        await service.RecordMovementAsync("t1", "biz-1", product.Id, StockMovementType.Restock, 5, "Supplier delivery", null, "user-1", ct: CancellationToken.None);

        var updated = await products.GetByIdAsync(product.Id, CancellationToken.None);
        Assert.Equal(15, updated!.StockQuantity);
        var movement = Assert.Single(await movements.FindAsync(m => m.ProductId == product.Id, CancellationToken.None));
        Assert.Equal(5, movement.QuantityDelta);
        Assert.Equal(StockMovementType.Restock, movement.Type);
    }

    [Fact]
    public async Task GetLowStockAsync_OnlyReturnsProductsAtOrBelowThreshold()
    {
        var (service, products, _) = Create();
        products.Seed(
            new Product { BusinessId = "biz-1", Name = "Low", TrackInventory = true, StockQuantity = 2, ReorderThreshold = 5 },
            new Product { BusinessId = "biz-1", Name = "Fine", TrackInventory = true, StockQuantity = 10, ReorderThreshold = 5 },
            new Product { BusinessId = "biz-1", Name = "Untracked", TrackInventory = false, StockQuantity = 0, ReorderThreshold = 5 },
            new Product { BusinessId = "biz-1", Name = "NoThreshold", TrackInventory = true, StockQuantity = 0, ReorderThreshold = null });

        var lowStock = await service.GetLowStockAsync("biz-1", CancellationToken.None);

        var entry = Assert.Single(lowStock);
        Assert.Equal("Low", entry.ProductName);
    }

    [Fact]
    public async Task GetValuationAsync_ValuesStockAtCostAndAtRetail_OverallAndPerCategory()
    {
        var (service, products, _) = Create();
        products.Seed(
            new Product { BusinessId = "biz-1", CategoryId = "cat-1", StockQuantity = 10, Price = 5m, CostPrice = 3m },
            new Product { BusinessId = "biz-1", CategoryId = "cat-1", StockQuantity = 2, Price = 20m, CostPrice = 12m },
            new Product { BusinessId = "biz-1", CategoryId = "cat-2", StockQuantity = 1, Price = 100m, CostPrice = 60m });

        var result = await service.GetValuationAsync("biz-1", CancellationToken.None);

        // §9.31: cost is the accounting figure. Retail is reported alongside it but is not an
        // asset value — reporting only retail is exactly the defect this replaced.
        Assert.Equal(114m, result.TotalValueAtCost);   // (10*3) + (2*12) + (1*60)
        Assert.Equal(190m, result.TotalValueAtRetail); // (10*5) + (2*20) + (1*100)
        Assert.Equal(76m, result.PotentialMargin);
        Assert.Equal(2, result.ByCategory.Count);
        Assert.Contains(result.ByCategory, c => c.CategoryId == "cat-1" && c.ValueAtCost == 54m && c.ValueAtRetail == 90m);
        Assert.Contains(result.ByCategory, c => c.CategoryId == "cat-2" && c.ValueAtCost == 60m && c.ValueAtRetail == 100m);
    }

    [Fact]
    public async Task GetValuationAsync_CountsProductsWithNoCostPriceInsteadOfValuingThemAtZero()
    {
        var (service, products, _) = Create();
        products.Seed(
            new Product { BusinessId = "biz-1", CategoryId = "cat-1", StockQuantity = 10, Price = 5m, CostPrice = 3m },
            new Product { BusinessId = "biz-1", CategoryId = "cat-1", StockQuantity = 4, Price = 25m, CostPrice = null });

        var result = await service.GetValuationAsync("biz-1", CancellationToken.None);

        // "We don't know what this cost" and "it was free" are different facts, and a balance
        // sheet must not conflate them — hence the separate count rather than a silent zero.
        Assert.Equal(30m, result.TotalValueAtCost);
        Assert.Equal(1, result.UnvaluedProductCount);
    }

    [Fact]
    public async Task RecordMovementAsync_RefusesToTakeStockNegative()
    {
        var (service, products, movements) = Create();
        var product = products.Seed(new Product { BusinessId = "biz-1", Name = "Widget", StockQuantity = 3 })[0];

        await Assert.ThrowsAsync<ConflictException>(() => service.RecordMovementAsync(
            "t1", "biz-1", product.Id, StockMovementType.DamageWriteOff, -5, "Write-off", ct: CancellationToken.None));

        // The guard is what keeps the movement ledger and the balance in agreement: a rejected
        // adjustment must leave no audit row behind claiming it happened.
        Assert.Equal(3, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);
        Assert.Empty(await movements.FindAsync(m => m.ProductId == product.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TryConsumeStockAsync_ReturnsFalseRatherThanThrowing_WhenStockIsShort()
    {
        var (service, products, movements) = Create();
        var product = products.Seed(new Product { BusinessId = "biz-1", StockQuantity = 2 })[0];

        var ok = await service.TryConsumeStockAsync("t1", "biz-1", product.Id, null, 5, "Checkout", "ORD-1", null, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(2, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);
        Assert.Empty(await movements.FindAsync(m => m.ProductId == product.Id, CancellationToken.None));
    }

    [Fact]
    public async Task TryConsumeStockAsync_DecrementsVariantStock_WhenAVariantIsNamed()
    {
        var (service, products, _) = Create();
        var product = products.Seed(new Product
        {
            BusinessId = "biz-1",
            StockQuantity = 100,
            Variants = [new ProductVariant { Id = "v1", AttributeSummary = "Red / L", StockQuantity = 4 }]
        })[0];

        var ok = await service.TryConsumeStockAsync("t1", "biz-1", product.Id, "v1", 3, "Checkout", "ORD-1", null, CancellationToken.None);

        Assert.True(ok);
        var updated = await products.GetByIdAsync(product.Id, CancellationToken.None);
        Assert.Equal(1, updated!.Variants[0].StockQuantity);
        // The product-level pool is untouched — variant stock is its own balance (§9.22).
        Assert.Equal(100, updated.StockQuantity);
    }

    [Theory]
    [InlineData(StockMovementType.Sale)]
    [InlineData(StockMovementType.Return)]
    public async Task AdjustStockAsync_RejectsSystemGeneratedTypes(StockMovementType type)
    {
        var (service, products, _) = Create();
        var product = products.Seed(new Product { BusinessId = "biz-1" })[0];

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.AdjustStockAsync("t1", "biz-1", product.Id, new AdjustStockRequest(5, "test", type), "user-1", CancellationToken.None));
    }

    [Fact]
    public async Task AdjustStockAsync_AllowsDamageWriteOff()
    {
        var (service, products, _) = Create();
        var product = products.Seed(new Product { BusinessId = "biz-1", StockQuantity = 10 })[0];

        var result = await service.AdjustStockAsync("t1", "biz-1", product.Id,
            new AdjustStockRequest(-3, "Damaged in warehouse", StockMovementType.DamageWriteOff), "user-1", CancellationToken.None);

        Assert.Equal(7, result.StockQuantity);
    }
}
