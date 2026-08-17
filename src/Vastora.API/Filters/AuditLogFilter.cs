using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.API.Filters;

/// <summary>
/// §9.35. Records every mutating request — who, what, when, and what came back.
///
/// One interception point rather than an <c>IAuditLogService.Record(...)</c> call inside each
/// service, deliberately: a call site can be forgotten when a new endpoint is added, and that is
/// exactly how audit trails rot. The trade-off is that entries are HTTP-shaped (route + method +
/// status) rather than domain-shaped (before/after field values) — enough to answer "who deleted
/// this product and when", which is what was missing, and honest about not being more.
///
/// Reads are skipped: logging every GET would bury the writes that matter in noise.
/// </summary>
public class AuditLogFilter(IMongoRepository<AuditLogEntry> auditLog, ILogger<AuditLogFilter> logger) : IAsyncActionFilter
{
    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!MutatingMethods.Contains(request.Method))
        {
            await next();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var executed = await next();
        stopwatch.Stop();

        try
        {
            await WriteAsync(context, executed, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // The audit write must never turn a successful operation into a failed response.
            // Logged loudly instead, because a silently missing audit trail is its own problem.
            logger.LogError(ex, "Failed to write audit log entry for {Method} {Path}", request.Method, request.Path);
        }
    }

    private async Task WriteAsync(ActionExecutingContext context, ActionExecutedContext executed, long elapsedMs)
    {
        var http = context.HttpContext;
        var user = http.User;

        var entry = new AuditLogEntry
        {
            TenantId = ResolveTenantId(http),
            BusinessId = RouteValue(context, "businessId") ?? string.Empty,
            UserId = user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub) ?? string.Empty,
            UserEmail = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email) ?? string.Empty,
            Role = user.FindFirstValue(ClaimTypes.Role) ?? "Anonymous",
            Method = http.Request.Method,
            Path = http.Request.Path.Value ?? string.Empty,
            RouteTemplate = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?.AttributeRouteInfo?.Template ?? string.Empty,
            StatusCode = executed.HttpContext.Response.StatusCode,
            IpAddress = http.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = http.Request.Headers.UserAgent.ToString(),
            // Best-effort: whichever id-shaped route value this endpoint targets. Enough to make
            // "who deleted product X" answerable without parsing the path by hand.
            ResourceId = RouteValue(context, "productId")
                         ?? RouteValue(context, "orderId")
                         ?? RouteValue(context, "categoryId")
                         ?? RouteValue(context, "couponId")
                         ?? RouteValue(context, "userId")
                         ?? RouteValue(context, "id"),
            DurationMs = elapsedMs
        };

        await auditLog.AddAsync(entry, http.RequestAborted);
    }

    /// <summary>
    /// Prefers the tenant the authorization handler resolved for the target Business, because a
    /// PlatformSuperAdmin's own TenantId claim is empty — see HttpContextTenantExtensions.
    /// </summary>
    private static string ResolveTenantId(HttpContext http)
    {
        var resolved = Authorization.HttpContextTenantExtensions.GetResolvedTenantId(http);
        return string.IsNullOrEmpty(resolved)
            ? http.User.FindFirstValue(Application.Common.VastoraClaimTypes.TenantId) ?? string.Empty
            : resolved;
    }

    private static string? RouteValue(ActionExecutingContext context, string key) =>
        context.RouteData.Values.TryGetValue(key, out var value) ? value?.ToString() : null;
}
