using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.39. A long-lived server-to-server credential, scoped to one Tenant (and optionally one
/// Business). Hashed at rest and shown exactly once at creation — the plaintext is
/// unrecoverable, same discipline as every other secret in this codebase. Existed nowhere
/// before: the only credential was a 30-minute user JWT, which no integration can use.
/// </summary>
public class ApiKey : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Empty = the key spans every Business under the Tenant.</summary>
    public string BusinessId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Non-secret public half, sent as the key's identity so lookup doesn't need a full scan.</summary>
    public string KeyId { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    /// <summary>Coarse scopes: "read", "write". Absent scope = denied.</summary>
    public List<string> Scopes { get; set; } = ["read"];

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public bool IsUsable(DateTime now) =>
        RevokedAt is null && !IsDeleted && (ExpiresAt is null || ExpiresAt > now);
}
