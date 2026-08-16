using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// A single account table for every role in the system. Scoping fields are populated
/// according to role:
///   PlatformSuperAdmin -> TenantId/BusinessId both empty.
///   TenantOwner         -> TenantId set, BusinessId empty (spans every Business under the tenant).
///   BusinessAdmin/Staff/DeliveryAgent/Customer -> TenantId and BusinessId both set.
/// </summary>
public class AppUser : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Customer;

    public UserStatus Status { get; set; } = UserStatus.PendingVerification;

    public List<Address> Addresses { get; set; } = [];

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// §9.34. Null means the address was never proved. Before this existed, UserStatus.PendingVerification
    /// was the enum default and unreachable — every account was created Active with an unverified email,
    /// including the address the password-reset flow trusts.
    /// </summary>
    public DateTime? EmailVerifiedAt { get; set; }

    public DateTime? PhoneVerifiedAt { get; set; }

    public bool IsEmailVerified => EmailVerifiedAt is not null;

    /// <summary>§9.36. Transactional mail ignores these; marketing mail must honour them.</summary>
    public NotificationPreferences NotificationPreferences { get; set; } = new();

    /// <summary>Stable per-user token embedded in unsubscribe links, so opting out needs no login.</summary>
    public string UnsubscribeToken { get; set; } = string.Empty;

    /// <summary>§9.23. Cached for pricing; the authority is CustomerGroup.CustomerUserIds.</summary>
    public List<string> CustomerGroupIds { get; set; } = [];

    /// <summary>
    /// §9.37. Set when the customer exercised their right to erasure. PII is overwritten in place
    /// rather than the document being deleted, because orders reference this id and destroying it
    /// would orphan the merchant's own financial records.
    /// </summary>
    public DateTime? AnonymizedAt { get; set; }
}

/// <summary>§9.36. Per-channel marketing consent. Defaults are opt-out for marketing, opt-in for transactional.</summary>
public class NotificationPreferences
{
    /// <summary>Order confirmations and status changes. Cannot be disabled — it is the record of a purchase.</summary>
    public bool TransactionalEmail { get; set; } = true;

    /// <summary>Abandoned-cart nudges, promotions, newsletters. Opt-in — sending without this is a legal problem, not a rude one.</summary>
    public bool MarketingEmail { get; set; }

    public bool BackInStockAlerts { get; set; } = true;

    public bool ReviewRequests { get; set; } = true;

    public bool MarketingSms { get; set; }

    public DateTime? MarketingConsentAt { get; set; }
}
