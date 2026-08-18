using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Promotions;

/// <inheritdoc cref="IPromotionService"/>
public class PromotionService(
    IMongoRepository<Promotion> promotions,
    IMongoRepository<Order> orders) : IPromotionService
{
    public async Task<PromotionEvaluation> EvaluateAsync(PromotionContext context, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var candidates = await promotions.FindAsync(p => p.BusinessId == context.BusinessId && p.IsActive, ct);

        var live = candidates
            .Where(p => p.IsLiveNow(now))
            .OrderBy(p => p.Priority)
            .ToList();

        var discounts = new List<AppliedDiscount>();
        var appliedIds = new List<string>();
        var freeShipping = false;

        foreach (var promotion in live)
        {
            if (!await QualifiesAsync(promotion, context, ct))
            {
                continue;
            }

            var amount = ComputeDiscount(promotion, context);
            var isFreeShipping = promotion.Effect == PromotionEffect.FreeShipping;

            if (amount <= 0 && !isFreeShipping)
            {
                continue;
            }

            discounts.Add(new AppliedDiscount("Promotion", promotion.Name, amount, isFreeShipping));
            appliedIds.Add(promotion.Id);
            freeShipping |= isFreeShipping;

            // A non-stackable promotion is exclusive: it wins and nothing lower-priority runs.
            // Evaluated in Priority order above, so "first non-stackable that qualifies" is
            // deterministic rather than dependent on document order.
            if (!promotion.Stackable)
            {
                break;
            }
        }

        return new PromotionEvaluation(discounts, freeShipping, appliedIds);
    }

    private async Task<bool> QualifiesAsync(Promotion promotion, PromotionContext context, CancellationToken ct)
    {
        // A coded promotion only fires when its code was actually entered; an uncoded one is
        // automatic. This is the distinction Coupon could not express at all.
        if (!string.IsNullOrWhiteSpace(promotion.Code)
            && !context.EnteredCodes.Any(c => string.Equals(c, promotion.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (promotion.MinOrderAmount is { } min && ScopedSubtotal(promotion, context) < min)
        {
            return false;
        }

        if (promotion.CustomerGroupIds.Count > 0
            && !promotion.CustomerGroupIds.Intersect(context.CustomerGroupIds).Any())
        {
            return false;
        }

        if (promotion.FirstOrderOnly && !context.IsFirstOrder)
        {
            return false;
        }

        if (promotion.MaxUsesPerCustomer is { } perCustomer && !string.IsNullOrEmpty(context.CustomerUserId))
        {
            // Counted from real order history rather than a per-customer tally field, so it stays
            // correct even for promotions created after a customer already had orders.
            var used = await orders.CountAsync(
                o => o.BusinessId == context.BusinessId
                     && o.CustomerUserId == context.CustomerUserId
                     && o.AppliedPromotionIds.Contains(promotion.Id), ct);

            if (used >= perCustomer)
            {
                return false;
            }
        }

        return ScopedLines(promotion, context).Count > 0;
    }

    /// <summary>The lines a promotion is allowed to touch, per its Scope.</summary>
    private static List<PricedLine> ScopedLines(Promotion promotion, PromotionContext context) =>
        promotion.Scope switch
        {
            PromotionScope.Products => [.. context.Lines.Where(l => promotion.ProductIds.Contains(l.ProductId))],
            PromotionScope.Categories => [.. context.Lines.Where(l => promotion.CategoryIds.Contains(l.CategoryId))],
            _ => [.. context.Lines]
        };

    private static decimal ScopedSubtotal(Promotion promotion, PromotionContext context) =>
        ScopedLines(promotion, context).Sum(l => l.LineTotal);

    private static decimal ComputeDiscount(Promotion promotion, PromotionContext context)
    {
        var lines = ScopedLines(promotion, context);
        var scopedSubtotal = lines.Sum(l => l.LineTotal);

        return promotion.Effect switch
        {
            PromotionEffect.PercentageOff => Round(scopedSubtotal * promotion.Value / 100m),

            // Never let a fixed discount exceed what it's discounting — a $50-off code on a $30
            // basket must not produce a negative total.
            PromotionEffect.FixedAmountOff => Round(Math.Min(promotion.Value, scopedSubtotal)),

            PromotionEffect.FreeShipping => 0m,
            PromotionEffect.BuyXGetY => Round(ComputeBuyXGetY(promotion, lines)),
            _ => 0m
        };
    }

    /// <summary>
    /// Buy X get Y, settled against the *cheapest* qualifying units — the industry convention,
    /// and the one that can't be gamed by adding an expensive item to a cart of cheap ones.
    /// Units are expanded individually because a single line of quantity 6 must be able to
    /// satisfy a "buy 2 get 1" three times over.
    /// </summary>
    private static decimal ComputeBuyXGetY(Promotion promotion, List<PricedLine> lines)
    {
        if (promotion.BuyQuantity <= 0 || promotion.GetQuantity <= 0)
        {
            return 0m;
        }

        var units = lines
            .SelectMany(l => Enumerable.Repeat(l.UnitPrice, l.Quantity))
            .OrderBy(price => price)
            .ToList();

        var groupSize = promotion.BuyQuantity + promotion.GetQuantity;
        var completeGroups = units.Count / groupSize;
        if (completeGroups == 0)
        {
            return 0m;
        }

        var freeUnits = completeGroups * promotion.GetQuantity;
        return units.Take(freeUnits).Sum();
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task RegisterUsageAsync(IReadOnlyList<string> promotionIds, CancellationToken ct = default)
    {
        foreach (var id in promotionIds.Distinct())
        {
            var promotion = await promotions.GetByIdAsync(id, ct);
            if (promotion is null)
            {
                continue;
            }

            // Atomic, and capped where a cap exists — the read-then-write shape this replaces let
            // concurrent redemptions push a limited promotion past its MaxUses (§9.17).
            if (promotion.MaxUses is { } max)
            {
                await promotions.TryIncrementBelowAsync(id, p => p.UsedCount, max, ct);
            }
            else
            {
                await promotions.IncrementAsync(id, p => p.UsedCount, 1, ct);
            }
        }
    }

    public async Task<PagedResult<PromotionResponse>> GetAllAsync(string businessId, PageRequest page, CancellationToken ct = default)
    {
        var result = await promotions.FindPagedAsync(
            p => p.BusinessId == businessId, page, p => p.Priority, SortDirection.Ascending, ct);
        return result.Map(Map);
    }

    public async Task<List<PromotionResponse>> GetPublicLiveAsync(string businessId, CancellationToken ct = default)
    {
        var candidates = await promotions.FindAsync(
            p => p.BusinessId == businessId && p.IsActive && p.Code != null && p.Visibility == DiscountVisibility.Public, ct);
        return [.. candidates.Where(p => p.IsLiveNow(DateTime.UtcNow)).OrderBy(p => p.Priority).Select(Map)];
    }

    public async Task<PromotionResponse> CreateAsync(string tenantId, string businessId, CreatePromotionRequest request, CancellationToken ct = default)
    {
        await EnsureCodeIsFreeAsync(businessId, request.Code, null, ct);

        var promotion = new Promotion
        {
            TenantId = tenantId,
            BusinessId = businessId
        };

        Apply(promotion, request);
        await promotions.AddAsync(promotion, ct);
        return Map(promotion);
    }

    public async Task<PromotionResponse> UpdateAsync(string tenantId, string businessId, string promotionId, CreatePromotionRequest request, CancellationToken ct = default)
    {
        var promotion = await GetScopedAsync(tenantId, businessId, promotionId, ct);
        await EnsureCodeIsFreeAsync(businessId, request.Code, promotionId, ct);

        Apply(promotion, request);
        await promotions.UpdateAsync(promotion, ct);
        return Map(promotion);
    }

    public async Task DeleteAsync(string tenantId, string businessId, string promotionId, CancellationToken ct = default)
    {
        await GetScopedAsync(tenantId, businessId, promotionId, ct);
        await promotions.DeleteAsync(promotionId, ct: ct);
    }

    /// <summary>
    /// Enforced in code rather than by a unique index, because the code is nullable (automatic
    /// promotions have none) and a unique index over a nullable field would reject the second
    /// automatic promotion a business creates.
    /// </summary>
    private async Task EnsureCodeIsFreeAsync(string businessId, string? code, string? excludingId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var existing = await promotions.FindOneAsync(p => p.BusinessId == businessId && p.Code == code, ct);
        if (existing is not null && existing.Id != excludingId)
        {
            throw new ConflictException($"A promotion with code '{code}' already exists.");
        }
    }

    private static void Apply(Promotion promotion, CreatePromotionRequest request)
    {
        promotion.Name = request.Name;
        promotion.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
        promotion.Effect = request.Effect;
        promotion.Scope = request.Scope;
        promotion.Value = request.Value;
        promotion.ProductIds = request.ProductIds ?? [];
        promotion.CategoryIds = request.CategoryIds ?? [];
        promotion.BuyQuantity = request.BuyQuantity;
        promotion.GetQuantity = request.GetQuantity;
        promotion.MinOrderAmount = request.MinOrderAmount;
        promotion.CustomerGroupIds = request.CustomerGroupIds ?? [];
        promotion.FirstOrderOnly = request.FirstOrderOnly;
        promotion.MaxUses = request.MaxUses;
        promotion.MaxUsesPerCustomer = request.MaxUsesPerCustomer;
        promotion.Priority = request.Priority;
        promotion.Stackable = request.Stackable;
        promotion.StartsAt = request.StartsAt;
        promotion.EndsAt = request.EndsAt;
        promotion.IsActive = request.IsActive;
        promotion.Visibility = request.Visibility;
    }

    private async Task<Promotion> GetScopedAsync(string tenantId, string businessId, string promotionId, CancellationToken ct)
    {
        var promotion = await promotions.GetByIdAsync(promotionId, ct);
        if (promotion is null || promotion.TenantId != tenantId || promotion.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Promotion), promotionId);
        }

        return promotion;
    }

    private static PromotionResponse Map(Promotion p) => new(
        p.Id, p.Name, p.Code, p.Effect, p.Scope, p.Value, p.ProductIds, p.CategoryIds,
        p.BuyQuantity, p.GetQuantity, p.MinOrderAmount, p.CustomerGroupIds, p.FirstOrderOnly,
        p.MaxUses, p.UsedCount, p.MaxUsesPerCustomer, p.Priority, p.Stackable,
        p.StartsAt, p.EndsAt, p.IsActive, p.IsLiveNow(DateTime.UtcNow), p.Visibility);
}
