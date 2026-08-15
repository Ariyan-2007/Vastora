namespace Vastora.Application.Accounting;

public record ExpenseResponse(
    string Id, string BusinessId, string Category, decimal Amount, string Note, DateTime IncurredAt, string CreatedByUserId);

public record CreateExpenseRequest(string Category, decimal Amount, string Note, DateTime IncurredAt);

public record UpdateExpenseRequest(string Category, decimal Amount, string Note, DateTime IncurredAt);

/// <summary>
/// §9.16c. NetProfit = Revenue − Refunds − Expenses − DeliveryPayouts, all summed within
/// [From, To]. Single-entry, cash-basis — not a GAAP-compliant statement.
/// </summary>
public record ProfitAndLossResponse(
    DateTime From, DateTime To, decimal Revenue, decimal Refunds, decimal Expenses, decimal DeliveryPayouts, decimal NetProfit);

/// <summary>
/// §9.16c. CashPosition is all-time Revenue − Refunds − Expenses − DeliveryPayouts (not windowed
/// like the P&L above — a balance sheet is a snapshot, not a period). InventoryValue comes from
/// §9.15d. TotalAssets is just their sum — no liabilities are tracked, so this is a partial
/// balance sheet, not a complete one.
/// </summary>
public record BalanceSheetResponse(decimal CashPosition, decimal InventoryValue, decimal TotalAssets);
