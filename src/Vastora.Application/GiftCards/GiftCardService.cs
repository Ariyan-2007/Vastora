using System.Security.Cryptography;
using System.Text;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.GiftCards;

/// <inheritdoc cref="IGiftCardService"/>
public class GiftCardService(
    IMongoRepository<GiftCard> giftCards,
    IMongoRepository<Business> businesses) : IGiftCardService
{
    public async Task<GiftCardResponse> IssueAsync(string tenantId, string businessId, IssueGiftCardRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new ConflictException("A gift card must be issued for a positive amount.");
        }

        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        var code = GenerateCode();
        var card = new GiftCard
        {
            TenantId = tenantId,
            BusinessId = businessId,
            CodeHash = Hash(code),
            CodeSuffix = code[^4..],
            InitialBalance = request.Amount,
            RemainingBalance = request.Amount,
            Currency = business.Currency,
            IssuedToEmail = request.IssuedToEmail,
            ExpiresAt = request.ExpiresAt
        };

        await giftCards.AddAsync(card, ct);

        // The only time the plaintext code is ever returned. Not stored, not recoverable.
        return Map(card, code);
    }

    public async Task<PagedResult<GiftCardResponse>> GetAllAsync(string businessId, PageRequest page, CancellationToken ct = default)
    {
        var result = await giftCards.FindPagedAsync(g => g.BusinessId == businessId, page, g => g.CreatedAt, ct: ct);
        return result.Map(c => Map(c, null));
    }

    public async Task<GiftCardBalanceResponse> CheckBalanceAsync(string businessId, string code, CancellationToken ct = default)
    {
        var card = await FindByCodeAsync(businessId, code, ct)
            ?? throw new NotFoundException(nameof(GiftCard), "code");

        return new GiftCardBalanceResponse(
            card.CodeSuffix, card.RemainingBalance, card.Currency, card.ExpiresAt, card.IsRedeemableNow(DateTime.UtcNow));
    }

    public async Task DeactivateAsync(string tenantId, string businessId, string giftCardId, CancellationToken ct = default)
    {
        var card = await giftCards.GetByIdAsync(giftCardId, ct);
        if (card is null || card.TenantId != tenantId || card.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(GiftCard), giftCardId);
        }

        card.IsActive = false;
        await giftCards.UpdateAsync(card, ct);
    }

    public async Task<GiftCardSettlement> QuoteAsync(string businessId, IReadOnlyList<string> codes, decimal amountDue, CancellationToken ct = default)
    {
        var uses = new List<OrderGiftCardUse>();
        var remaining = amountDue;
        var now = DateTime.UtcNow;

        foreach (var code in codes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (remaining <= 0)
            {
                break;
            }

            var card = await FindByCodeAsync(businessId, code, ct);
            if (card is null || !card.IsRedeemableNow(now))
            {
                // A dead or unknown code is skipped rather than fatal: failing the whole checkout
                // because one of three cards expired would be a worse experience than covering
                // what can be covered and charging the rest.
                continue;
            }

            var applied = Math.Min(card.RemainingBalance, remaining);
            remaining -= applied;

            uses.Add(new OrderGiftCardUse
            {
                GiftCardId = card.Id,
                CodeSuffix = card.CodeSuffix,
                AmountApplied = applied
            });
        }

        return new GiftCardSettlement(uses, uses.Sum(u => u.AmountApplied));
    }

    public async Task RedeemAsync(IReadOnlyList<OrderGiftCardUse> uses, CancellationToken ct = default)
    {
        foreach (var use in uses)
        {
            var card = await giftCards.GetByIdAsync(use.GiftCardId, ct);
            if (card is null)
            {
                continue;
            }

            // Clamped rather than trusted: the quote was taken before checkout committed, and
            // the same card could have been spent elsewhere in between. Worst case the customer
            // is charged the difference, never the card overdrawn.
            var applied = Math.Min(use.AmountApplied, card.RemainingBalance);
            card.RemainingBalance -= applied;
            await giftCards.UpdateAsync(card, ct);
        }
    }

    public async Task RefundAsync(IReadOnlyList<OrderGiftCardUse> uses, CancellationToken ct = default)
    {
        foreach (var use in uses)
        {
            var card = await giftCards.GetByIdAsync(use.GiftCardId, ct);
            if (card is null)
            {
                continue;
            }

            card.RemainingBalance += use.AmountApplied;
            if (card.RemainingBalance > card.InitialBalance)
            {
                card.RemainingBalance = card.InitialBalance;
            }

            await giftCards.UpdateAsync(card, ct);
        }
    }

    /// <summary>
    /// Lookup is by hash, so the plaintext never has to exist server-side beyond this call. The
    /// hash is unsalted SHA-256 deliberately — unlike a password it must be *findable* by value,
    /// and the code is 16 random characters rather than a guessable human secret.
    /// </summary>
    private async Task<GiftCard?> FindByCodeAsync(string businessId, string code, CancellationToken ct)
    {
        var hash = Hash(code.Trim().ToUpperInvariant());
        return await giftCards.FindOneAsync(g => g.BusinessId == businessId && g.CodeHash == hash, ct);
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no O/0/I/1 — these get read aloud
        var bytes = RandomNumberGenerator.GetBytes(16);
        return new string([.. bytes.Select(b => alphabet[b % alphabet.Length])]);
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static GiftCardResponse Map(GiftCard c, string? plaintextCode) => new(
        c.Id, plaintextCode, c.CodeSuffix, c.InitialBalance, c.RemainingBalance,
        c.Currency, c.IssuedToEmail, c.ExpiresAt, c.IsActive, c.CreatedAt);
}
