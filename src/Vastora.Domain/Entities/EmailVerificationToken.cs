using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.34. Deliberately the same shape as PasswordResetToken — hashed at rest, expiring,
/// single-use — because that pattern is already proven here and a verification token is exactly
/// as sensitive as a reset token: both grant account control to whoever holds them.
/// </summary>
public class EmailVerificationToken : BaseEntity
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>The email being proved, captured at issue time so a mid-flight address change invalidates the proof.</summary>
    public string Email { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public bool IsActive(DateTime now) => UsedAt is null && ExpiresAt > now;
}
