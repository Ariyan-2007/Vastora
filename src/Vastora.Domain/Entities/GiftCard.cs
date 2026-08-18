using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.24. Sold like a product, spent like cash. Stored as an issued balance rather than a
/// product row because it is a *liability* — the money is received before the goods are.
/// §9.48: this never writes a ledger entry of its own — issuance is a staff-only action
/// (<c>MerchandisingController.IssueGiftCard</c>) with no captured payment to book, and
/// redemption needs no entry either, since the order it pays for already books full Revenue at
/// delivery regardless of tender type. <c>GetBalanceSheetAsync</c>'s gift-card liability is
/// instead a live sum of every active card's <see cref="RemainingBalance"/>, which the redemption
/// path already keeps correct.
/// </summary>
public class GiftCard : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    /// <summary>Hashed at rest — the plaintext code is shown once at issue and never stored, same as a reset token.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Last 4 characters, kept in the clear so staff and the owner can identify a card without the full code.</summary>
    public string CodeSuffix { get; set; } = string.Empty;

    public decimal InitialBalance { get; set; }

    public decimal RemainingBalance { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string? IssuedToEmail { get; set; }

    /// <summary>The order that bought it, when it was purchased rather than manually issued.</summary>
    public string? SourceOrderId { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsRedeemableNow(DateTime now) =>
        IsActive && RemainingBalance > 0 && (ExpiresAt is null || ExpiresAt > now);
}
