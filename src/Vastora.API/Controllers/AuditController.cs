using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

public record AuditLogResponse(
    string Id,
    string UserId,
    string UserEmail,
    string Role,
    string Method,
    string Path,
    string RouteTemplate,
    int StatusCode,
    string IpAddress,
    string? ResourceId,
    long DurationMs,
    DateTime CreatedAt);

/// <summary>
/// §9.35. Who changed what, in one Business. Admin-tier only — the audit trail names the staff
/// who performed each action, and in a multi-staff BackOffice that is exactly the kind of record
/// staff should not be able to review (or quietly check) about one another.
/// </summary>
[Tags("BackOffice - Audit")]
[Route("api/businesses/{businessId}/audit-log")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
[Authorize(Policy = "BusinessMember")]
public class AuditController(ICurrentUserContext currentUser, IMongoRepository<AuditLogEntry> auditLog)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogResponse>>> GetAll(
        string businessId,
        [FromQuery] string? userId,
        [FromQuery] string? resourceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await auditLog.FindPagedAsync(
            a => a.BusinessId == businessId
                 && (userId == null || a.UserId == userId)
                 && (resourceId == null || a.ResourceId == resourceId)
                 && (from == null || a.CreatedAt >= from)
                 && (to == null || a.CreatedAt <= to),
            PageRequest.Of(page, pageSize), a => a.CreatedAt, ct: ct);

        return Ok(result.Map(a => new AuditLogResponse(
            a.Id, a.UserId, a.UserEmail, a.Role, a.Method, a.Path, a.RouteTemplate,
            a.StatusCode, a.IpAddress, a.ResourceId, a.DurationMs, a.CreatedAt)));
    }
}
