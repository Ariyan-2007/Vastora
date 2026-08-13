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
}
