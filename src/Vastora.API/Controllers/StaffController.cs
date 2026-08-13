using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Auth;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Users;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>BackOffice management of a Business's staff (BusinessAdmin, BusinessStaff, DeliveryAgent) and its customers.</summary>
[Route("api/businesses/{businessId}")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
public class StaffController(ICurrentUserContext currentUser, IUserService userService, IBusinessService businessService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet("staff")]
    public async Task<ActionResult<List<UserSummaryResponse>>> GetStaff(string businessId, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await userService.GetBusinessStaffAsync(tenantId, businessId, ct);
        return Ok(result);
    }

    [HttpPost("staff")]
    public async Task<ActionResult<UserSummaryResponse>> CreateStaff(string businessId, CreateStaffRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await userService.CreateStaffAsync(tenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPatch("staff/{userId}/status")]
    public async Task<ActionResult<UserSummaryResponse>> UpdateStaffStatus(string businessId, string userId, [FromBody] UserStatus status, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await userService.UpdateStatusAsync(tenantId, userId, status, ct);
        return Ok(result);
    }

    [HttpGet("customers")]
    public async Task<ActionResult<List<UserSummaryResponse>>> GetCustomers(string businessId, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await userService.GetBusinessCustomersAsync(tenantId, businessId, ct);
        return Ok(result);
    }
}
