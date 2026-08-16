using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Returns;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// BackOffice returns handling — §9.21. Deciding a return is Staff-permitted (it is day-to-day
/// order work), but <c>refund</c> is Admin-tier: it moves money out, which §9.3 puts on the same
/// footing as coupons and accounting.
/// </summary>
[Tags("BackOffice - Returns")]
[Route("api/businesses/{businessId}/returns")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class ReturnsController(ICurrentUserContext currentUser, IReturnService returnService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReturnResponse>>> GetAll(
        string businessId, [FromQuery] ReturnStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await returnService.GetForBusinessAsync(businessId, status, PageRequest.Of(page, pageSize), ct));

    [HttpGet("{returnId}")]
    public async Task<ActionResult<ReturnResponse>> GetById(string businessId, string returnId, CancellationToken ct) =>
        Ok(await returnService.GetByIdAsync(ResolvedTenantId, businessId, returnId, ct));

    [HttpPost("{returnId}/decision")]
    public async Task<ActionResult<ReturnResponse>> Decide(string businessId, string returnId, DecideReturnRequest request, CancellationToken ct) =>
        Ok(await returnService.DecideAsync(ResolvedTenantId, businessId, returnId, request, CurrentUser.UserId, ct));

    /// <summary>Goods physically back with the seller. This — not approval, and not the refund — is what restocks.</summary>
    [HttpPost("{returnId}/received")]
    public async Task<ActionResult<ReturnResponse>> MarkReceived(string businessId, string returnId, CancellationToken ct) =>
        Ok(await returnService.MarkReceivedAsync(ResolvedTenantId, businessId, returnId, CurrentUser.UserId, ct));

    [HttpPost("{returnId}/refund")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<ActionResult<ReturnResponse>> Refund(string businessId, string returnId, CancellationToken ct) =>
        Ok(await returnService.RefundAsync(ResolvedTenantId, businessId, returnId, CurrentUser.UserId, ct));
}
