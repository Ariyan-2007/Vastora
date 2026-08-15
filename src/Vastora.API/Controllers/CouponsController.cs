using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[Tags("BackOffice - Coupons")]
[Route("api/businesses/{businessId}/coupons")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class CouponsController(ICurrentUserContext currentUser, ICouponService couponService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<CouponResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        var result = await couponService.GetForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<ActionResult<CouponResponse>> Create(string businessId, CreateCouponRequest request, CancellationToken ct)
    {
        var result = await couponService.CreateAsync(ResolvedTenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPut("{couponId}")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<ActionResult<CouponResponse>> Update(string businessId, string couponId, UpdateCouponRequest request, CancellationToken ct)
    {
        var result = await couponService.UpdateAsync(ResolvedTenantId, businessId, couponId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{couponId}")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<IActionResult> Delete(string businessId, string couponId, CancellationToken ct)
    {
        await couponService.DeleteAsync(ResolvedTenantId, businessId, couponId, ct);
        return NoContent();
    }
}
