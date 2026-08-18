using Vastora.Domain.Enums;

namespace Vastora.Application.GiftCards;

/// <summary>
/// §9.24. An append-only credit ledger per customer. The balance is always the sum of the
/// entries — never a mutable field — so two concurrent awards can't lose one another, and every
/// movement stays explicable after the fact.
/// </summary>
public interface IStoreCreditService
{
    Task<decimal> GetBalanceAsync(string businessId, string customerUserId, CancellationToken ct = default);

    Task<StoreCreditBalanceResponse> GetStatementAsync(string businessId, string customerUserId, CancellationToken ct = default);

    Task RecordAsync(
        string tenantId,
        string businessId,
        string customerUserId,
        decimal signedAmount,
        StoreCreditReason reason,
        string note,
        string? referenceOrderId = null,
        string? referenceReturnId = null,
        /// <summary>§9.43. Set only on a promotional grant meant to lapse; leave null for a refund settlement, which should never expire.</summary>
        DateTime? expiresAt = null,
        CancellationToken ct = default);

    /// <summary>Spends up to <paramref name="requested"/>, capped at the live balance. Returns what was actually spent.</summary>
    Task<decimal> SpendAsync(
        string tenantId,
        string businessId,
        string customerUserId,
        decimal requested,
        string referenceOrderId,
        CancellationToken ct = default);
}
