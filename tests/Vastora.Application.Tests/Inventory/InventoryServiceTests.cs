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
        var productService = new ProductService(products, tenants);
        return (new InventoryService(products, movements, productService), products, movements);
    }

    [Fact]
    public async Task RecordMovementAsync_UpdatesStockAndLogsMovement()
    {
        var (service, products, movements) = Create();
        var product = products.Seed(new Product { BusinessId = "biz-1", StockQuantity = 10 })[0];

        await service.RecordMovementAsync("t1", "biz-1", product.Id, StockMovementType.Restock, 5, "Supplier delivery", null, "user-1", CancellationToken.None);

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
    public async Task GetValuationAsync_SumsStockQuantityTimesPrice_OverallAndPerCategory()
    {
        var (service, products, _) = Create();
        products.Seed(
            new Product { BusinessId = "biz-1", CategoryId = "cat-1", StockQuantity = 10, Price = 5m },
            new Product { BusinessId = "biz-1", CategoryId = "cat-1", StockQuantity = 2, Price = 20m },
            new Product { BusinessId = "biz-1", CategoryId = "cat-2", StockQuantity = 1, Price = 100m });

        var result = await service.GetValuationAsync("biz-1", CancellationToken.None);

        Assert.Equal(190m, result.TotalValue); // (10*5) + (2*20) + (1*100)
        Assert.Equal(2, result.ByCategory.Count);
        Assert.Contains(result.ByCategory, c => c.CategoryId == "cat-1" && c.Value == 90m);
        Assert.Contains(result.ByCategory, c => c.CategoryId == "cat-2" && c.Value == 100m);
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
