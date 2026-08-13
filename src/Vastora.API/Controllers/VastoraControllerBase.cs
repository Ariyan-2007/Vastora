using Microsoft.AspNetCore.Mvc;
using Vastora.API.Authorization;
using Vastora.Application.Common.Interfaces;

namespace Vastora.API.Controllers;

[ApiController]
public abstract class VastoraControllerBase(ICurrentUserContext currentUser) : ControllerBase
{
    protected ICurrentUserContext CurrentUser => currentUser;

    /// <summary>
    /// The target Business's real TenantId, resolved by the "BusinessMember" authorization
    /// policy before this action ran (see BusinessAccessAuthorizationHandler). Only valid on
    /// actions whose controller/method carries [Authorize(Policy = "BusinessMember")].
    /// </summary>
    protected string ResolvedTenantId => HttpContext.GetResolvedTenantId();
}
