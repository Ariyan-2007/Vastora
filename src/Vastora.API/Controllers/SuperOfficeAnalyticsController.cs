using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Analytics;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>Cross-business revenue/order/top-product rollup for a TenantOwner — §9.8.</summary>
[Tags("SuperOffice")]
[Route("api/superoffice/analytics")]
[Authorize(Roles = nameof(UserRole.TenantOwner))]
public class SuperOfficeAnalyticsController(ICurrentUserContext currentUser, IAnalyticsService analyticsService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<TenantAnalyticsResponse>> GetAnalytics(CancellationToken ct)
    {
        var result = await analyticsService.GetTenantAnalyticsAsync(CurrentUser.TenantId, ct);
        return Ok(result);
    }
}
