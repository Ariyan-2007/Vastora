using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Vastora.Application.Businesses;
using Vastora.Application.Common;
using Vastora.Domain.Enums;

namespace Vastora.API.Authorization;

/// <summary>
/// Backs the "BusinessMember" policy: decides whether the current user may act on the Business
/// named by the request's {businessId} route value, and — on success — stashes that Business's
/// real TenantId on HttpContext for the action to read back (see HttpContextTenantExtensions).
///
///   BusinessAdmin / BusinessStaff / DeliveryAgent -> only their own BusinessId (from the JWT).
///   TenantOwner                                    -> any Business under their own Tenant (SuperOffice).
///   PlatformSuperAdmin                              -> any Business at all.
///
/// A mismatched BusinessId for the first group simply leaves the requirement unsatisfied ->
/// the framework's default handler turns that into 403. A TenantOwner/PlatformSuperAdmin
/// pointing at a Business that doesn't exist, or doesn't belong to their Tenant, lets
/// NotFoundException propagate out of this handler and up through ExceptionHandlingMiddleware
/// as a 404 — deliberately indistinguishable from "that Business doesn't exist", matching the
/// behavior this replaces.
/// </summary>
public class BusinessAccessAuthorizationHandler(IHttpContextAccessor httpContextAccessor, IBusinessService businessService)
    : AuthorizationHandler<BusinessMemberRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, BusinessMemberRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        if (!httpContext.Request.RouteValues.TryGetValue("businessId", out var raw) || raw is not string businessId || businessId.Length == 0)
        {
            return;
        }

        if (!Enum.TryParse<UserRole>(context.User.FindFirstValue(ClaimTypes.Role), out var role))
        {
            return;
        }

        var tenantIdClaim = context.User.FindFirstValue(VastoraClaimTypes.TenantId) ?? string.Empty;
        var businessIdClaim = context.User.FindFirstValue(VastoraClaimTypes.BusinessId) ?? string.Empty;

        switch (role)
        {
            case UserRole.BusinessAdmin or UserRole.BusinessStaff or UserRole.DeliveryAgent:
                if (businessIdClaim == businessId)
                {
                    httpContext.SetResolvedTenantId(tenantIdClaim);
                    context.Succeed(requirement);
                }
                break;

            case UserRole.TenantOwner:
                await businessService.GetByIdAsync(tenantIdClaim, businessId);
                httpContext.SetResolvedTenantId(tenantIdClaim);
                context.Succeed(requirement);
                break;

            case UserRole.PlatformSuperAdmin:
                var business = await businessService.GetByIdForPlatformAsync(businessId);
                httpContext.SetResolvedTenantId(business.TenantId);
                context.Succeed(requirement);
                break;
        }
    }
}
