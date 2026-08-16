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
        var stockStore = new FakeProductStockStore(products);
        var inventoryService = new InventoryService(products, new FakeMongoRepository<StockMovement>(), stockStore, productService);

        var service = new AccountingService(
            ledgerEntries, expenses,
            new FakeMongoRepository<GiftCard>(),
            new FakeMongoRepository<Order>(),
            new FakeMongoRepository<Business>(),
            new FakeMongoRepository<ReturnRequest>(),
            products,
            inventoryService);

        return (service, ledgerEntries, expenses, products);
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
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.CostOfGoodsSold, Amount = 200m, OccurredAt = inWindow },
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 9999m, OccurredAt = outsideWindow });
        expenses.Seed(new Expense { BusinessId = "biz-1", Amount = 100m, IncurredAt = inWindow });

        var result = await service.GetProfitAndLossAsync("biz-1",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.Equal(500m, result.Revenue);
        Assert.Equal(50m, result.Refunds);
        Assert.Equal(200m, result.CostOfGoodsSold);
        Assert.Equal(100m, result.Expenses);
        Assert.Equal(25m, result.DeliveryPayouts);

        // §9.31: COGS is subtracted now. The old NetProfit here was 325 — inflated by the entire
        // 200 the goods cost, which is precisely the defect this replaced.
        Assert.Equal(250m, result.GrossProfit);  // 500 - 50 - 200
        Assert.Equal(125m, result.NetProfit);    // 250 - 100 - 25
        Assert.Equal(55.56m, result.GrossMarginPercent); // 250 / 450
    }

    [Fact]
    public async Task GetProfitAndLossAsync_FlagsRevenueOrdersThatContributedNoCost()
    {
        var (service, ledgerEntries, _, _) = Create();
        var at = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        ledgerEntries.Seed(
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 100m, ReferenceOrderId = "ORD-1", OccurredAt = at },
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.CostOfGoodsSold, Amount = 40m, ReferenceOrderId = "ORD-1", OccurredAt = at },
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 80m, ReferenceOrderId = "ORD-2", OccurredAt = at });

        var result = await service.GetProfitAndLossAsync("biz-1",
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        // ORD-2 sold something with no recorded cost, so GrossProfit is optimistic by an unknown
        // amount. Surfacing the count beats presenting a confident wrong number.
        Assert.Equal(1, result.UncostedOrderCount);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_ValuesInventoryAtCost_NotRetail()
    {
        var (service, ledgerEntries, expenses, products) = Create();
        ledgerEntries.Seed(new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 1000m });
        expenses.Seed(new Expense { BusinessId = "biz-1", Amount = 200m });
        products.Seed(new Product { BusinessId = "biz-1", StockQuantity = 10, Price = 15m, CostPrice = 9m });

        var result = await service.GetBalanceSheetAsync("biz-1", CancellationToken.None);

        Assert.Equal(800m, result.CashPosition); // 1000 - 200
        // §9.31: 90 (at cost), not 150 (at retail). Using retail overstated assets by the entire
        // unrealised margin on every business's balance sheet.
        Assert.Equal(90m, result.InventoryValueAtCost);
        Assert.Equal(150m, result.InventoryValueAtRetail);
        Assert.Equal(890m, result.TotalAssets);
    }

    [Fact]
    public async Task GetBalanceSheetAsync_CarriesTaxCollectedAsALiability_NotAsProfit()
    {
        var (service, ledgerEntries, _, _) = Create();
        ledgerEntries.Seed(
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.Revenue, Amount = 1000m },
            new LedgerEntry { BusinessId = "biz-1", Type = LedgerEntryType.TaxCollected, Amount = 150m });

        var result = await service.GetBalanceSheetAsync("biz-1", CancellationToken.None);

        // The cash is genuinely in the account, but it is owed onward — so it appears on both
        // sides and nets out of the business's actual position.
        Assert.Equal(1150m, result.CashPosition);
        Assert.Equal(150m, result.TaxPayable);
        Assert.Equal(1000m, result.NetPosition);
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
