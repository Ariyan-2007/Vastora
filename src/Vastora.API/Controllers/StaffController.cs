using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Auth;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Users;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>BackOffice management of a Business's staff (BusinessAdmin, BusinessStaff, DeliveryAgent) and its customers.</summary>
[Tags("BackOffice - Staff")]
[Route("api/businesses/{businessId}")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
[Authorize(Policy = "BusinessMember")]
public class StaffController(ICurrentUserContext currentUser, IUserService userService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet("staff")]
    public async Task<ActionResult<List<UserSummaryResponse>>> GetStaff(string businessId, CancellationToken ct)
    {
        var result = await userService.GetBusinessStaffAsync(ResolvedTenantId, businessId, ct);
        return Ok(result);
    }

    [HttpPost("staff")]
    public async Task<ActionResult<UserSummaryResponse>> CreateStaff(string businessId, CreateStaffRequest request, CancellationToken ct)
    {
        var result = await userService.CreateStaffAsync(ResolvedTenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPatch("staff/{userId}/status")]
    public async Task<ActionResult<UserSummaryResponse>> UpdateStaffStatus(string businessId, string userId, [FromBody] UserStatus status, CancellationToken ct)
    {
        var result = await userService.UpdateStatusAsync(ResolvedTenantId, userId, status, ct);
        return Ok(result);
    }

    [HttpGet("customers")]
    public async Task<ActionResult<List<UserSummaryResponse>>> GetCustomers(string businessId, CancellationToken ct)
    {
        var result = await userService.GetBusinessCustomersAsync(ResolvedTenantId, businessId, ct);
        return Ok(result);
    }
}
