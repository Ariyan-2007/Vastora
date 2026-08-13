using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[ApiController]
public abstract class VastoraControllerBase(ICurrentUserContext currentUser) : ControllerBase
{
    protected ICurrentUserContext CurrentUser => currentUser;

    /// <summary>
    /// Confirms the caller may act on the given Business and returns its owning TenantId.
    ///   BusinessAdmin / BusinessStaff / DeliveryAgent -> only their own BusinessId.
    ///   TenantOwner                                    -> any Business under their own Tenant (SuperOffice).
    ///   PlatformSuperAdmin                              -> any Business at all.
    /// </summary>
    protected async Task<string> EnsureBusinessAccessAsync(string businessId, IBusinessService businessService)
    {
        switch (currentUser.Role)
        {
            case UserRole.BusinessAdmin or UserRole.BusinessStaff or UserRole.DeliveryAgent:
                if (currentUser.BusinessId != businessId)
                {
                    throw new ForbiddenException();
                }
                return currentUser.TenantId;

            case UserRole.TenantOwner:
                await businessService.GetByIdAsync(currentUser.TenantId, businessId);
                return currentUser.TenantId;

            case UserRole.PlatformSuperAdmin:
                var business = await businessService.GetByIdForPlatformAsync(businessId);
                return business.TenantId;

            default:
                throw new ForbiddenException();
        }
    }
}
