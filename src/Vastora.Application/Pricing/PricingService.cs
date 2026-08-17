using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Application.CustomerGroups;
using Vastora.Application.GiftCards;
using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Application.Tax;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Pricing;

/// <inheritdoc cref="IPricingService"/>
public class PricingService(
    IMongoRepository<Product> products,
    IMongoRepository<Order> orders,
    ICouponService couponService,
    IPromotionService promotionService,
    ICustomerGroupService customerGroupService,
    IShippingService shippingService,
    ITaxService taxService,
    IGiftCardService giftCardService,
    IStoreCreditService storeCreditService) : IPricingService
{
    public async Task<IReadOnlyList<ResolvedLine>> ResolveLinesAsync(
        string businessId,
        IReadOnlyList<CartItem> items,
        string? customerUserId,
        CancellationToken ct = default)
    {
        var groupDiscount = string.IsNullOrEmpty(customerUserId)
            ? 0m
            : await customerGroupService.GetBestDiscountPercentAsync(businessId, customerUserId, ct);

        var resolved = new List<ResolvedLine>();

        foreach (var item in items)
        {
            var product = await products.GetByIdAsync(item.ProductId, ct);
            if (product is null || product.BusinessId != businessId)
            {
                throw new NotFoundException(nameof(Product), item.ProductId);
            }

            ProductVariant? variant = null;
            if (item.VariantId is not null)
            {
                variant = product.Variants.FirstOrDefault(v => v.Id == item.VariantId)
                    ?? throw new NotFoundException("ProductVariant", item.VariantId);
            }

            // §9.22: the variant's PriceOverride is the price when one is set. Before this,
            // Product.EffectivePrice ignored it entirely and every variant sold at the base price.
            var basePrice = variant?.PriceOverride ?? product.EffectivePrice;

            var unitPrice = groupDiscount > 0
                ? Math.Round(basePrice * (1 - groupDiscount / 100m), 2, MidpointRounding.AwayFromZero)
                : basePrice;

            resolved.Add(new ResolvedLine(
                product,
                variant,
                product.Id,
                variant?.Id,
                product.Name,
                variant?.AttributeSummary,
                unitPrice,
                product.CostPrice,
                item.Quantity,
                product.WeightKg ?? 0m));
        }

        return resolved;
    }

    public async Task<PriceBreakdown> PriceAsync(
        PricingContext context,
        IReadOnlyList<ResolvedLine> lines,
        CancellationToken ct = default)
    {
        var business = context.Business;
        var subtotal = Round(lines.Sum(l => l.LineTotal));

        var (discounts, promotionIds, freeShipping) = await ComputeDiscountsAsync(context, lines, subtotal, ct);
        var discountTotal = Round(Math.Min(discounts.Sum(d => d.Amount), subtotal));

        var totalWeight = lines.Sum(l => l.WeightKg * l.Quantity);
        var shippingOptions = await shippingService.GetQuotesAsync(
            business.Id, context.ShippingAddress, subtotal, totalWeight, ct);

        var (deliveryFee, methodName) = await shippingService.ResolveFeeAsync(
            business, context.ShippingAddress, subtotal, totalWeight,
            context.SelectedShippingRateId, context.ExplicitDeliveryFee, ct);

        if (freeShipping)
        {
            deliveryFee = 0m;
            methodName = methodName is null ? "Free shipping" : $"{methodName} (free)";
        }

        var tax = QuoteTax(business, lines, subtotal, discountTotal, deliveryFee);

        // Tax-inclusive pricing means the tax is already inside the line prices, so adding it to
        // the total again would double-charge it. Exclusive pricing adds it on top.
        var total = Round(subtotal - discountTotal + deliveryFee + (tax.PricesIncludeTax ? 0m : tax.TotalTax));
        if (total < 0)
        {
            total = 0m;
        }

        var settlement = context.GiftCardCodes.Count > 0
            ? await giftCardService.QuoteAsync(business.Id, context.GiftCardCodes, total, ct)
            : new GiftCardSettlement([], 0m);

        var afterGiftCards = Round(total - settlement.TotalApplied);

        var storeCredit = 0m;
        if (context.ApplyStoreCredit && !string.IsNullOrEmpty(context.CustomerUserId) && afterGiftCards > 0)
        {
            var balance = await storeCreditService.GetBalanceAsync(business.Id, context.CustomerUserId, ct);
            storeCredit = Round(Math.Min(balance, afterGiftCards));
        }

        var amountDue = Round(Math.Max(0m, afterGiftCards - storeCredit));

        return new PriceBreakdown(
            lines, subtotal, discounts, discountTotal, deliveryFee, methodName, tax, total,
            settlement.TotalApplied, storeCredit, amountDue, business.Currency,
            promotionIds, settlement.Uses, shippingOptions);
    }

    private async Task<(List<AppliedDiscount> Discounts, List<string> PromotionIds, bool FreeShipping)> ComputeDiscountsAsync(
        PricingContext context,
        IReadOnlyList<ResolvedLine> lines,
        decimal subtotal,
        CancellationToken ct)
    {
        var discounts = new List<AppliedDiscount>();

        var groups = string.IsNullOrEmpty(context.CustomerUserId)
            ? []
            : await customerGroupService.GetForCustomerAsync(context.Business.Id, context.CustomerUserId, ct);

        var isFirstOrder = string.IsNullOrEmpty(context.CustomerUserId)
            || await orders.CountAsync(
                o => o.BusinessId == context.Business.Id && o.CustomerUserId == context.CustomerUserId, ct) == 0;

        var promotionContext = new PromotionContext(
            context.Business.Id,
            context.CustomerUserId,
            [.. groups.Select(g => g.Id)],
            isFirstOrder,
            [.. lines.Select(l => new PricedLine(l.ProductId, l.Product.CategoryId, l.UnitPrice, l.Quantity))],
            context.PromotionCodes);

        var evaluation = await promotionService.EvaluateAsync(promotionContext, ct);
        discounts.AddRange(evaluation.Discounts);

        // The legacy Coupon path is evaluated after promotions and on the already-discounted
        // base, so a coupon and a promotion can't each take their cut of the full subtotal and
        // together exceed it. Kept as its own path (rather than migrated into Promotion) so every
        // coupon created before §9.23 keeps behaving exactly as it did.
        if (!string.IsNullOrWhiteSpace(context.CouponCode))
        {
            var remaining = Math.Max(0m, subtotal - discounts.Sum(d => d.Amount));
            var couponDiscount = await couponService.ValidateAndPriceAsync(
                context.Business.Id, context.CouponCode, remaining, ct);

            if (couponDiscount > 0)
            {
                discounts.Add(new AppliedDiscount("Coupon", context.CouponCode, Round(couponDiscount), false));
            }
        }

        return (discounts, [.. evaluation.AppliedPromotionIds], evaluation.FreeShipping);
    }

    /// <summary>
    /// Tax is quoted on the *discounted* base, apportioned across lines in proportion to their
    /// share of the subtotal. Taxing the pre-discount amount would overcharge the customer on
    /// every discounted order, which is both wrong and, in most jurisdictions, illegal.
    /// </summary>
    private TaxQuote QuoteTax(
        Business business,
        IReadOnlyList<ResolvedLine> lines,
        decimal subtotal,
        decimal discountTotal,
        decimal deliveryFee)
    {
        if (!business.Tax.Enabled || subtotal <= 0)
        {
            return TaxQuote.None;
        }

        var discountRatio = discountTotal <= 0 ? 1m : Math.Max(0m, 1m - discountTotal / subtotal);

        var taxableLines = lines
            .Select(l => (l.Product.TaxClass ?? string.Empty, Amount: Round(l.LineTotal * discountRatio)))
            .ToList();

        return taxService.Quote(business, taxableLines, deliveryFee);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
