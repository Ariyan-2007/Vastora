namespace Vastora.Application.Accounting;

public interface IAccountingService
{
    Task<ExpenseResponse> CreateExpenseAsync(string tenantId, string businessId, CreateExpenseRequest request, string createdByUserId, CancellationToken ct = default);

    Task<List<ExpenseResponse>> GetExpensesForBusinessAsync(string businessId, CancellationToken ct = default);

    Task<ExpenseResponse> UpdateExpenseAsync(string tenantId, string businessId, string expenseId, UpdateExpenseRequest request, CancellationToken ct = default);

    Task DeleteExpenseAsync(string tenantId, string businessId, string expenseId, CancellationToken ct = default);

    /// <summary>Sums LedgerEntry (§9.16a) and Expense (§9.16b) rows within [from, to] — §9.16c.</summary>
    Task<ProfitAndLossResponse> GetProfitAndLossAsync(string businessId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>All-time cash position plus current inventory valuation (§9.15d) — §9.16c.</summary>
    Task<BalanceSheetResponse> GetBalanceSheetAsync(string businessId, CancellationToken ct = default);
}
