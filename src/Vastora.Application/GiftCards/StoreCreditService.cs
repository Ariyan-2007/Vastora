using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.GiftCards;

/// <inheritdoc cref="IStoreCreditService"/>
public class StoreCreditService(
    IMongoRepository<StoreCreditEntry> entries,
    IMongoRepository<Business> businesses) : IStoreCreditService
{
    public async Task<decimal> GetBalanceAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var all = await entries.FindAsync(
            e => e.BusinessId == businessId && e.CustomerUserId == customerUserId, ct);
        return all.Sum(e => e.Amount);
    }

    public async Task<StoreCreditBalanceResponse> GetStatementAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var all = await entries.FindAsync(
            e => e.BusinessId == businessId && e.CustomerUserId == customerUserId, ct);

        var business = await businesses.GetByIdAsync(businessId, ct);

        var recent = all
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .Select(e => new StoreCreditEntryResponse(
                e.Id, e.Amount, e.Currency, e.Reason, e.Note, e.ReferenceOrderId, e.CreatedAt))
            .ToList();

        return new StoreCreditBalanceResponse(all.Sum(e => e.Amount), business?.Currency ?? string.Empty, recent);
    }

    public async Task RecordAsync(
        string tenantId,
        string businessId,
        string customerUserId,
        decimal signedAmount,
        StoreCreditReason reason,
        string note,
        string? referenceOrderId = null,
        string? referenceReturnId = null,
        CancellationToken ct = default)
    {
        if (signedAmount == 0)
        {
            return;
        }

        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        await entries.AddAsync(new StoreCreditEntry
        {
            TenantId = tenantId,
            BusinessId = businessId,
            CustomerUserId = customerUserId,
            Amount = signedAmount,
            Currency = business.Currency,
            Reason = reason,
            Note = note,
            ReferenceOrderId = referenceOrderId,
            ReferenceReturnId = referenceReturnId
        }, ct);
    }

    public async Task<decimal> SpendAsync(
        string tenantId,
        string businessId,
        string customerUserId,
        decimal requested,
        string referenceOrderId,
        CancellationToken ct = default)
    {
        if (requested <= 0)
        {
            return 0m;
        }

        var balance = await GetBalanceAsync(businessId, customerUserId, ct);
        var spend = Math.Min(balance, requested);
        if (spend <= 0)
        {
            return 0m;
        }

        await RecordAsync(tenantId, businessId, customerUserId, -spend, StoreCreditReason.Spent,
            $"Applied to order {referenceOrderId}", referenceOrderId, ct: ct);

        return spend;
    }
}
