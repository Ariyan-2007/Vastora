using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[Route("api/businesses/{businessId}/coupons")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
public class CouponsController(ICurrentUserContext currentUser, ICouponService couponService, IBusinessService businessService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<CouponResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await couponService.GetForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CouponResponse>> Create(string businessId, CreateCouponRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await couponService.CreateAsync(tenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPut("{couponId}")]
    public async Task<ActionResult<CouponResponse>> Update(string businessId, string couponId, UpdateCouponRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await couponService.UpdateAsync(tenantId, businessId, couponId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{couponId}")]
    public async Task<IActionResult> Delete(string businessId, string couponId, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        await couponService.DeleteAsync(tenantId, businessId, couponId, ct);
        return NoContent();
    }
}
