using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public string UserId { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByTokenId { get; set; }

    public string CreatedByIp { get; set; } = string.Empty;

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
