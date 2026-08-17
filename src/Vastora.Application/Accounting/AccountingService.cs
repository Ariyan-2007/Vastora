using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Inventory;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Accounting;

public class AccountingService(
    IMongoRepository<LedgerEntry> ledgerEntries,
    IMongoRepository<Expense> expenses,
    IMongoRepository<GiftCard> giftCards,
    IMongoRepository<Order> orders,
    IMongoRepository<Business> businesses,
    IMongoRepository<ReturnRequest> returnRequests,
    IInventoryService inventoryService) : IAccountingService
{
    public async Task<ExpenseResponse> CreateExpenseAsync(string tenantId, string businessId, CreateExpenseRequest request, string createdByUserId, CancellationToken ct = default)
    {
        var expense = new Expense
        {
            TenantId = tenantId,
            BusinessId = businessId,
            Category = request.Category,
            Amount = request.Amount,
            Note = request.Note,
            IncurredAt = request.IncurredAt,
            CreatedByUserId = createdByUserId
        };

        await expenses.AddAsync(expense, ct);
        return Map(expense);
    }

    public async Task<List<ExpenseResponse>> GetExpensesForBusinessAsync(string businessId, CancellationToken ct = default)
    {
        var list = await expenses.FindAsync(e => e.BusinessId == businessId, ct);
        return list.OrderByDescending(e => e.IncurredAt).Select(Map).ToList();
    }

    public async Task<ExpenseResponse> UpdateExpenseAsync(string tenantId, string businessId, string expenseId, UpdateExpenseRequest request, CancellationToken ct = default)
    {
        var expense = await GetScopedExpenseAsync(tenantId, businessId, expenseId, ct);

        expense.Category = request.Category;
        expense.Amount = request.Amount;
        expense.Note = request.Note;
        expense.IncurredAt = request.IncurredAt;
        expense.UpdatedAt = DateTime.UtcNow;

        await expenses.UpdateAsync(expense, ct);
        return Map(expense);
    }

    public async Task DeleteExpenseAsync(string tenantId, string businessId, string expenseId, CancellationToken ct = default)
    {
        await GetScopedExpenseAsync(tenantId, businessId, expenseId, ct);
        await expenses.DeleteAsync(expenseId, ct: ct);
    }

    public async Task<ProfitAndLossResponse> GetProfitAndLossAsync(string businessId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var entries = await ledgerEntries.FindAsync(l => l.BusinessId == businessId && l.OccurredAt >= from && l.OccurredAt <= to, ct);
        var expenseList = await expenses.FindAsync(e => e.BusinessId == businessId && e.IncurredAt >= from && e.IncurredAt <= to, ct);

        var revenue = Sum(entries, LedgerEntryType.Revenue);
        var refunds = Sum(entries, LedgerEntryType.Refund);
        var cogs = Sum(entries, LedgerEntryType.CostOfGoodsSold);
        var deliveryPayouts = Sum(entries, LedgerEntryType.DeliveryPayout);
        var taxCollected = Sum(entries, LedgerEntryType.TaxCollected);
        var expenseTotal = expenseList.Sum(e => e.Amount);

        var grossProfit = revenue - refunds - cogs;
        var netProfit = grossProfit - expenseTotal - deliveryPayouts;

        var netRevenue = revenue - refunds;
        var grossMargin = netRevenue <= 0
            ? 0m
            : Math.Round(grossProfit / netRevenue * 100m, 2, MidpointRounding.AwayFromZero);

        // How many revenue-bearing orders in this window contributed no cost at all. A non-zero
        // count means GrossProfit is optimistic by an unknown amount, and saying so beats
        // presenting a confident wrong number.
        var revenueOrders = entries
            .Where(l => l.Type == LedgerEntryType.Revenue)
            .Select(l => l.ReferenceOrderId)
            .ToHashSet();

        var costedOrders = entries
            .Where(l => l.Type == LedgerEntryType.CostOfGoodsSold)
            .Select(l => l.ReferenceOrderId)
            .ToHashSet();

        var uncosted = revenueOrders.Count(o => !costedOrders.Contains(o));

        return new ProfitAndLossResponse(
            from, to, revenue, refunds, cogs, grossProfit, grossMargin,
            expenseTotal, deliveryPayouts, netProfit, taxCollected, uncosted);
    }

    public async Task<BalanceSheetResponse> GetBalanceSheetAsync(string businessId, CancellationToken ct = default)
    {
        var entries = await ledgerEntries.FindAsync(l => l.BusinessId == businessId, ct);
        var expenseList = await expenses.FindAsync(e => e.BusinessId == businessId, ct);

        var revenue = Sum(entries, LedgerEntryType.Revenue);
        var refunds = Sum(entries, LedgerEntryType.Refund);
        var cogs = Sum(entries, LedgerEntryType.CostOfGoodsSold);
        var deliveryPayouts = Sum(entries, LedgerEntryType.DeliveryPayout);
        var taxCollected = Sum(entries, LedgerEntryType.TaxCollected);
        var expenseTotal = expenseList.Sum(e => e.Amount);

        // Cash includes the tax collected — the money is genuinely in the account — but the same
        // amount is carried as a liability below, because it is owed to the tax authority and was
        // never the business's to spend.
        var cashPosition = revenue + taxCollected - refunds - cogs - expenseTotal - deliveryPayouts;

        var valuation = await inventoryService.GetValuationAsync(businessId, ct);

        var giftCardFloat = (await giftCards.FindAsync(g => g.BusinessId == businessId && g.IsActive, ct))
            .Sum(g => g.RemainingBalance);

        var totalAssets = cashPosition + valuation.TotalValueAtCost;
        var totalLiabilities = taxCollected + giftCardFloat;

        return new BalanceSheetResponse(
            Round(cashPosition),
            Round(valuation.TotalValueAtCost),
            Round(valuation.TotalValueAtRetail),
            Round(totalAssets),
            Round(taxCollected),
            Round(giftCardFloat),
            Round(totalLiabilities),
            Round(totalAssets - totalLiabilities));
    }

    private static decimal Sum(List<LedgerEntry> entries, LedgerEntryType type) =>
        entries.Where(l => l.Type == type).Sum(l => l.Amount);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<BusinessDashboardResponse> GetDashboardAsync(string businessId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        var windowOrders = await orders.FindAsync(
            o => o.BusinessId == businessId && o.PlacedAt >= from && o.PlacedAt <= to, ct);

        // Revenue is recognised on Delivered only — deliberately the same rule §9.8's SuperOffice
        // analytics and §9.16a's sales ledger use, so these three numbers can never quietly
        // disagree with one another.
        var delivered = windowOrders.Where(o => o.Status == OrderStatus.Delivered).ToList();
        var revenue = delivered.Sum(o => o.Total - o.TaxAmount - o.RefundedAmount);
        var cogs = delivered.Sum(o => o.Items.Sum(i => i.LineCost ?? 0m));

        // Order count is broader than revenue: a still-Processing order is real pipeline activity
        // worth seeing before it becomes revenue.
        var counted = windowOrders.Where(o => o.Status != OrderStatus.Cancelled).ToList();

        var customerIds = counted
            .Where(o => !string.IsNullOrEmpty(o.CustomerUserId))
            .Select(o => o.CustomerUserId)
            .ToList();

        var uniqueCustomers = customerIds.Distinct().Count();
        var repeatCustomers = customerIds.GroupBy(id => id).Count(g => g.Count() > 1);

        // "New" means their first-ever order with this business landed inside the window — checked
        // against all-time history, not just the window, or every customer would look new.
        var newCustomers = 0;
        foreach (var customerId in customerIds.Distinct())
        {
            var firstOrder = (await orders.FindAsync(
                o => o.BusinessId == businessId && o.CustomerUserId == customerId, ct))
                .OrderBy(o => o.PlacedAt)
                .FirstOrDefault();

            if (firstOrder is not null && firstOrder.PlacedAt >= from)
            {
                newCustomers++;
            }
        }

        var dailySales = delivered
            .GroupBy(o => o.PlacedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new SalesPointResponse(g.Key, Round(g.Sum(o => o.Total - o.TaxAmount)), g.Count()))
            .ToList();

        var topProducts = delivered
            .SelectMany(o => o.Items)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new TopProductResponse(
                g.Key.ProductId, g.Key.ProductName, g.Sum(i => i.Quantity), Round(g.Sum(i => i.LineTotal))))
            .OrderByDescending(p => p.QuantitySold)
            .Take(10)
            .ToList();

        var statusBreakdown = windowOrders
            .GroupBy(o => o.Status)
            .Select(g => new OrderStatusCountResponse(g.Key.ToString(), g.Count()))
            .OrderBy(s => s.Status)
            .ToList();

        var pendingReturns = (int)await returnRequests.CountAsync(
            r => r.BusinessId == businessId
                 && (r.Status == ReturnStatus.Requested || r.Status == ReturnStatus.Approved), ct);

        var lowStock = (await inventoryService.GetLowStockAsync(businessId, ct)).Count;

        var orderCount = counted.Count;
        var averageOrderValue = orderCount == 0 ? 0m : Round(counted.Sum(o => o.Total) / orderCount);

        return new BusinessDashboardResponse(
            from, to,
            Round(revenue),
            Round(revenue - cogs),
            orderCount,
            delivered.Count,
            windowOrders.Count(o => o.Status == OrderStatus.Cancelled),
            averageOrderValue,
            uniqueCustomers,
            repeatCustomers,
            uniqueCustomers == 0 ? 0m : Round(repeatCustomers / (decimal)uniqueCustomers * 100m),
            newCustomers,
            pendingReturns,
            lowStock,
            dailySales,
            topProducts,
            statusBreakdown,
            business.Currency);
    }

    private async Task<Expense> GetScopedExpenseAsync(string tenantId, string businessId, string expenseId, CancellationToken ct)
    {
        var expense = await expenses.GetByIdAsync(expenseId, ct);
        if (expense is null || expense.BusinessId != businessId || expense.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Expense), expenseId);
        }

        return expense;
    }

    private static ExpenseResponse Map(Expense e) => new(e.Id, e.BusinessId, e.Category, e.Amount, e.Note, e.IncurredAt, e.CreatedByUserId);
}
