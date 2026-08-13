using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.DeliveryAgents;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[Tags("BackOffice - Delivery Agents")]
[Route("api/businesses/{businessId}/delivery-agents")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)},{nameof(UserRole.DeliveryAgent)}")]
[Authorize(Policy = "BusinessMember")]
public class DeliveryAgentsController(ICurrentUserContext currentUser, IDeliveryAgentService deliveryAgentService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<List<DeliveryAgentResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        var result = await deliveryAgentService.GetForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    [HttpGet("me")]
    public async Task<ActionResult<DeliveryAgentResponse>> GetMe(string businessId, CancellationToken ct)
    {
        var result = await deliveryAgentService.GetByUserIdAsync(businessId, CurrentUser.UserId, ct);
        return Ok(result);
    }

    [HttpPatch("me/status")]
    public async Task<ActionResult<DeliveryAgentResponse>> UpdateMyStatus(string businessId, UpdateDeliveryAgentStatusRequest request, CancellationToken ct)
    {
        var result = await deliveryAgentService.UpdateStatusAsync(ResolvedTenantId, businessId, CurrentUser.UserId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{userId}/status")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
    public async Task<ActionResult<DeliveryAgentResponse>> UpdateStatus(string businessId, string userId, UpdateDeliveryAgentStatusRequest request, CancellationToken ct)
    {
        var result = await deliveryAgentService.UpdateStatusAsync(ResolvedTenantId, businessId, userId, request, ct);
        return Ok(result);
    }
}
