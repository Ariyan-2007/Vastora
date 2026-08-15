using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Inventory;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Accounting;

public class AccountingService(
    IMongoRepository<LedgerEntry> ledgerEntries,
    IMongoRepository<Expense> expenses,
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
        await expenses.DeleteAsync(expenseId, ct);
    }

    public async Task<ProfitAndLossResponse> GetProfitAndLossAsync(string businessId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var entries = await ledgerEntries.FindAsync(l => l.BusinessId == businessId && l.OccurredAt >= from && l.OccurredAt <= to, ct);
        var expenseList = await expenses.FindAsync(e => e.BusinessId == businessId && e.IncurredAt >= from && e.IncurredAt <= to, ct);

        var revenue = entries.Where(l => l.Type == LedgerEntryType.Revenue).Sum(l => l.Amount);
        var refunds = entries.Where(l => l.Type == LedgerEntryType.Refund).Sum(l => l.Amount);
        var deliveryPayouts = entries.Where(l => l.Type == LedgerEntryType.DeliveryPayout).Sum(l => l.Amount);
        var expenseTotal = expenseList.Sum(e => e.Amount);

        return new ProfitAndLossResponse(from, to, revenue, refunds, expenseTotal, deliveryPayouts,
            revenue - refunds - expenseTotal - deliveryPayouts);
    }

    public async Task<BalanceSheetResponse> GetBalanceSheetAsync(string businessId, CancellationToken ct = default)
    {
        var entries = await ledgerEntries.FindAsync(l => l.BusinessId == businessId, ct);
        var expenseList = await expenses.FindAsync(e => e.BusinessId == businessId, ct);

        var revenue = entries.Where(l => l.Type == LedgerEntryType.Revenue).Sum(l => l.Amount);
        var refunds = entries.Where(l => l.Type == LedgerEntryType.Refund).Sum(l => l.Amount);
        var deliveryPayouts = entries.Where(l => l.Type == LedgerEntryType.DeliveryPayout).Sum(l => l.Amount);
        var expenseTotal = expenseList.Sum(e => e.Amount);

        var cashPosition = revenue - refunds - expenseTotal - deliveryPayouts;
        var valuation = await inventoryService.GetValuationAsync(businessId, ct);

        return new BalanceSheetResponse(cashPosition, valuation.TotalValue, cashPosition + valuation.TotalValue);
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
