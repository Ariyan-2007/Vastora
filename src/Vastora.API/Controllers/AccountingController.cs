using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Accounting;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// Expenses, P&amp;L and balance sheet — §9.16. Admin/TenantOwner/Platform only, not Staff —
/// financial data is at least as sensitive as coupons (§9.3's reasoning for the same split).
/// </summary>
[Tags("BackOffice - Accounting")]
[Route("api/businesses/{businessId}")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
[Authorize(Policy = "BusinessMember")]
public class AccountingController(ICurrentUserContext currentUser, IAccountingService accountingService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet("expenses")]
    public async Task<ActionResult<List<ExpenseResponse>>> GetExpenses(string businessId, CancellationToken ct)
    {
        var result = await accountingService.GetExpensesForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    [HttpPost("expenses")]
    public async Task<ActionResult<ExpenseResponse>> CreateExpense(string businessId, CreateExpenseRequest request, CancellationToken ct)
    {
        var result = await accountingService.CreateExpenseAsync(ResolvedTenantId, businessId, request, CurrentUser.UserId, ct);
        return Ok(result);
    }

    [HttpPut("expenses/{expenseId}")]
    public async Task<ActionResult<ExpenseResponse>> UpdateExpense(string businessId, string expenseId, UpdateExpenseRequest request, CancellationToken ct)
    {
        var result = await accountingService.UpdateExpenseAsync(ResolvedTenantId, businessId, expenseId, request, ct);
        return Ok(result);
    }

    [HttpDelete("expenses/{expenseId}")]
    public async Task<IActionResult> DeleteExpense(string businessId, string expenseId, CancellationToken ct)
    {
        await accountingService.DeleteExpenseAsync(ResolvedTenantId, businessId, expenseId, ct);
        return NoContent();
    }

    [HttpGet("accounting/profit-and-loss")]
    public async Task<ActionResult<ProfitAndLossResponse>> GetProfitAndLoss(string businessId, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
    {
        var result = await accountingService.GetProfitAndLossAsync(businessId, from, to, ct);
        return Ok(result);
    }

    [HttpGet("accounting/balance-sheet")]
    public async Task<ActionResult<BalanceSheetResponse>> GetBalanceSheet(string businessId, CancellationToken ct)
    {
        var result = await accountingService.GetBalanceSheetAsync(businessId, ct);
        return Ok(result);
    }

    /// <summary>
    /// §9.32. The per-business dashboard a BusinessAdmin previously had no endpoint for at all —
    /// §9.8's analytics are TenantOwner-only, so the platform's most common user could list orders
    /// and add them up by hand or nothing. Defaults to the last 30 days.
    /// </summary>
    [HttpGet("analytics/dashboard")]
    public async Task<ActionResult<BusinessDashboardResponse>> GetDashboard(
        string businessId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var toDate = to ?? DateTime.UtcNow;
        var fromDate = from ?? toDate.AddDays(-30);

        var result = await accountingService.GetDashboardAsync(businessId, fromDate, toDate, ct);
        return Ok(result);
    }
}
