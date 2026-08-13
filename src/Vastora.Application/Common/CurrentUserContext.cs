using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.Application.Common;

/// <summary>Scoped per-request; populated once by CurrentUserMiddleware in the API layer.</summary>
public class CurrentUserContext : ICurrentUserContext
{
    public bool IsAuthenticated { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public string TenantId { get; private set; } = string.Empty;

    public string BusinessId { get; private set; } = string.Empty;

    public UserRole? Role { get; private set; }

    public void Set(string userId, string tenantId, string businessId, UserRole role)
    {
        UserId = userId;
        TenantId = tenantId;
        BusinessId = businessId;
        Role = role;
        IsAuthenticated = true;
    }
}
