namespace Vastora.API.Authorization;

/// <summary>
/// The TenantId a successful "BusinessMember" authorization resolved for the target Business
/// is stashed here by <see cref="BusinessAccessAuthorizationHandler"/> so the controller action
/// can reuse it without a second lookup (PlatformSuperAdmin's own TenantId claim is empty, so
/// it can't just read ICurrentUserContext.TenantId — it has to be the *Business's* tenant).
/// </summary>
public static class HttpContextTenantExtensions
{
    private const string ResolvedTenantIdKey = "Vastora.ResolvedTenantId";

    internal static void SetResolvedTenantId(this HttpContext context, string tenantId) =>
        context.Items[ResolvedTenantIdKey] = tenantId;

    public static string GetResolvedTenantId(this HttpContext context) =>
        context.Items.TryGetValue(ResolvedTenantIdKey, out var value) && value is string tenantId
            ? tenantId
            : string.Empty;
}
