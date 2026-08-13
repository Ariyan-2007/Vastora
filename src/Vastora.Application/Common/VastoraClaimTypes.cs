namespace Vastora.Application.Common;

/// <summary>Custom JWT claim names shared by the token issuer (Infrastructure) and the claims reader (API middleware).</summary>
public static class VastoraClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string BusinessId = "business_id";
}
