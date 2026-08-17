using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// A customer's rating of a Product — §9.25. Held Pending until a BackOffice Admin publishes it,
/// because an unmoderated review feed on a multi-tenant platform is a spam vector aimed at
/// businesses that did not choose to run one.
/// </summary>
public class Review : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string ProductId { get; set; } = string.Empty;

    public string CustomerUserId { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>1–5, validated at the DTO boundary.</summary>
    public int Rating { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

    /// <summary>
    /// True when this customer has a Delivered order containing this product. Checked at
    /// submission time against real order history, never accepted from the client.
    /// </summary>
    public bool IsVerifiedPurchase { get; set; }

    /// <summary>The order that proved the purchase, kept for dispute resolution.</summary>
    public string? VerifiedOrderId { get; set; }

    public string? MerchantReply { get; set; }

    public DateTime? MerchantRepliedAt { get; set; }

    public int HelpfulCount { get; set; }
}
