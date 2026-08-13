using Vastora.Domain.Enums;

namespace Vastora.Application.Common.Interfaces;

/// <summary>
/// Request-scoped snapshot of the caller, populated from JWT claims by
/// CurrentUserMiddleware early in the pipeline. Application services read this instead
/// of touching HttpContext, so every query gets scoped to the right Tenant/Business
/// without repeating that logic in every controller.
/// </summary>
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    string UserId { get; }

    string TenantId { get; }

    string BusinessId { get; }

    UserRole? Role { get; }

    void Set(string userId, string tenantId, string businessId, UserRole role);
}
