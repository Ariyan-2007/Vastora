using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// The TenantOwner's cross-business control panel. Meaningful once a Tenant runs more
/// than one Business: this is the only place that can see every Business they own at once.
/// </summary>
[Route("api/superoffice/businesses")]
[Authorize(Roles = nameof(UserRole.TenantOwner))]
public class SuperOfficeController(ICurrentUserContext currentUser, IBusinessService businessService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<BusinessResponse>>> GetAll(CancellationToken ct)
    {
        var result = await businessService.GetAllForTenantAsync(CurrentUser.TenantId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<BusinessResponse>> Create(CreateBusinessRequest request, CancellationToken ct)
    {
        var result = await businessService.CreateAsync(CurrentUser.TenantId, request, ct);
        return Ok(result);
    }

    [HttpGet("{businessId}")]
    public async Task<ActionResult<BusinessResponse>> GetById(string businessId, CancellationToken ct)
    {
        var result = await businessService.GetByIdAsync(CurrentUser.TenantId, businessId, ct);
        return Ok(result);
    }

    [HttpPut("{businessId}")]
    public async Task<ActionResult<BusinessResponse>> Update(string businessId, UpdateBusinessRequest request, CancellationToken ct)
    {
        var result = await businessService.UpdateAsync(CurrentUser.TenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{businessId}/status")]
    public async Task<ActionResult<BusinessResponse>> UpdateStatus(string businessId, [FromBody] BusinessStatus status, CancellationToken ct)
    {
        var result = await businessService.UpdateStatusAsync(CurrentUser.TenantId, businessId, status, ct);
        return Ok(result);
    }
}
