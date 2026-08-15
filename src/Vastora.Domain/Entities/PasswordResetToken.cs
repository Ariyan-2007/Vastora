using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>Hashed, expiring, single-use — same pattern as RefreshToken (§9.10).</summary>
public class PasswordResetToken : BaseEntity
{
    public string UserId { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public bool IsActive => UsedAt is null && DateTime.UtcNow < ExpiresAt;
}
