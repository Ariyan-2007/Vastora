using Vastora.Application.Accounting;
using Vastora.Application.Inventory;
using Vastora.Application.Products;
using Vastora.Application.Tenants;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Accounting;

public class AccountingServiceTests
{
    private static (AccountingService Service, FakeMongoRepository<LedgerEntry> LedgerEntries, FakeMongoRepository<Expense> Expenses, FakeMongoRepository<Product> Products) Create()
    {
        var ledgerEntries = new FakeMongoRepository<LedgerEntry>();
        var expenses = new FakeMongoRepository<Expense>();
        var products = new FakeMongoRepository<Product>();
        var tenants = new FakeMongoRepository<TenantAccount>();
        var productService = new ProductService(products, tenants);
        var inventoryService = new InventoryService(products, new FakeMongoRepository<StockMovement>(), productService);

        return (new AccountingService(ledgerEntries, expenses, inventoryService), ledgerEntries, expenses, products);
    }

    [Fact]
    public async Task GetProfitAndLossAsync_OnlyCountsEntriesInsideWindow_AndComputesNetProfit()
    {
        var (service, ledgerEntries, expenses, _) = Create();
        var inWindow = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var outsideWindow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        ledgerEntries.Seed(
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 500m, OccurredAt = inWindow },
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Refund, Amount = 50m, OccurredAt = inWindow },
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.DeliveryPayout, Amount = 25m, OccurredAt = inWindow },
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 9999m, OccurredAt = outsideWindow });
        expenses.Seed(new Expense { BusinessId = "biz-1", Amount = 100m, IncurredAt = inWindow });

        var result = await service.GetProfitAndLossAsync("biz-1",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.Equal(500m, result.Revenue);
        Assert.Equal(50m, result.Refunds);
        Assert.Equal(100m, result.Expenses);
        Assert.Equal(25m, result.DeliveryPayouts);
        Assert.Equal(325m, result.NetProfit); // 500 - 50 - 100 - 25
    }

    [Fact]
    public async Task GetBalanceSheetAsync_CombinesCashPositionWithInventoryValuation()
    {
        var (service, ledgerEntries, expenses, products) = Create();
        ledgerEntries.Seed(new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 1000m });
        expenses.Seed(new Expense { BusinessId = "biz-1", Amount = 200m });
        products.Seed(new Product { BusinessId = "biz-1", StockQuantity = 10, Price = 15m }); // valuation: 150

        var result = await service.GetBalanceSheetAsync("biz-1", CancellationToken.None);

        Assert.Equal(800m, result.CashPosition); // 1000 - 200
        Assert.Equal(150m, result.InventoryValue);
        Assert.Equal(950m, result.TotalAssets);
    }

    [Fact]
    public async Task CreateExpense_ThenUpdateThenDelete_RoundTrips()
    {
        var (service, _, _, _) = Create();

        var created = await service.CreateExpenseAsync("t1", "biz-1",
            new CreateExpenseRequest("Rent", 500m, "Monthly office rent", DateTime.UtcNow), "user-1", CancellationToken.None);
        Assert.Equal("Rent", created.Category);

        var updated = await service.UpdateExpenseAsync("t1", "biz-1", created.Id,
            new UpdateExpenseRequest("Rent", 550m, "Rent increased", DateTime.UtcNow), CancellationToken.None);
        Assert.Equal(550m, updated.Amount);

        await service.DeleteExpenseAsync("t1", "biz-1", created.Id, CancellationToken.None);
        Assert.Empty(await service.GetExpensesForBusinessAsync("biz-1", CancellationToken.None));
    }
}
