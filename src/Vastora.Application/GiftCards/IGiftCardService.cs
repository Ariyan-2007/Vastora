using Vastora.Application.Common;
using Vastora.Domain.Entities;

namespace Vastora.Application.GiftCards;

/// <summary>How much a set of gift card codes can actually cover, resolved before checkout commits.</summary>
public record GiftCardSettlement(IReadOnlyList<OrderGiftCardUse> Uses, decimal TotalApplied);

/// <summary>
/// §9.24. Gift cards are a payment instrument and a liability, not Revenue when issued. §9.48:
/// issuance is staff-only with no payment captured here to book, so it writes no ledger entry of
/// any kind — the balance sheet's gift-card liability is a live sum of RemainingBalance across
/// active cards instead (see GiftCard.cs). Codes are hashed at rest for the same reason refresh
/// tokens are: whoever holds one can spend it.
/// </summary>
public interface IGiftCardService
{
    Task<GiftCardResponse> IssueAsync(string tenantId, string businessId, IssueGiftCardRequest request, CancellationToken ct = default);

    Task<PagedResult<GiftCardResponse>> GetAllAsync(string businessId, PageRequest page, CancellationToken ct = default);

    /// <summary>Public balance check by code — the only way a holder can see what's left.</summary>
    Task<GiftCardBalanceResponse> CheckBalanceAsync(string businessId, string code, CancellationToken ct = default);

    Task DeactivateAsync(string tenantId, string businessId, string giftCardId, CancellationToken ct = default);

    /// <summary>
    /// Works out how much of <paramref name="amountDue"/> the given codes cover, without spending
    /// anything. Split from <see cref="RedeemAsync"/> so a cart preview never draws down a card.
    /// </summary>
    Task<GiftCardSettlement> QuoteAsync(string businessId, IReadOnlyList<string> codes, decimal amountDue, CancellationToken ct = default);

    /// <summary>Actually draws the balances down. Called once, at checkout.</summary>
    Task RedeemAsync(IReadOnlyList<OrderGiftCardUse> uses, CancellationToken ct = default);

    /// <summary>Puts value back on the cards used for an order, when that order is refunded (§9.21).</summary>
    Task RefundAsync(IReadOnlyList<OrderGiftCardUse> uses, CancellationToken ct = default);
}
