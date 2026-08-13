using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Middleware;

/// <summary>Runs after UseAuthentication: copies the validated JWT's claims into the request-scoped ICurrentUserContext.</summary>
public class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUserContext currentUser)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? string.Empty;
            var tenantId = context.User.FindFirstValue(VastoraClaimTypes.TenantId) ?? string.Empty;
            var businessId = context.User.FindFirstValue(VastoraClaimTypes.BusinessId) ?? string.Empty;
            var roleClaim = context.User.FindFirstValue(ClaimTypes.Role);

            if (!string.IsNullOrEmpty(userId) && Enum.TryParse<UserRole>(roleClaim, out var role))
            {
                currentUser.Set(userId, tenantId, businessId, role);
            }
        }

        await next(context);
    }
}
