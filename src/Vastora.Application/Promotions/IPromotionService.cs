using Vastora.Application.Common;

namespace Vastora.Application.Promotions;

/// <summary>A cart line, flattened to what discount evaluation actually needs.</summary>
public record PricedLine(string ProductId, string CategoryId, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>Everything the promotion engine needs to decide, gathered once by the pricing service.</summary>
public record PromotionContext(
    string BusinessId,
    string? CustomerUserId,
    IReadOnlyList<string> CustomerGroupIds,
    bool IsFirstOrder,
    IReadOnlyList<PricedLine> Lines,
    IReadOnlyList<string> EnteredCodes)
{
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
}

public record PromotionEvaluation(IReadOnlyList<AppliedDiscount> Discounts, bool FreeShipping, IReadOnlyList<string> AppliedPromotionIds)
{
    public decimal TotalDiscount => Discounts.Sum(d => d.Amount);

    public static PromotionEvaluation None => new([], false, []);
}

/// <summary>
/// §9.23. The general discount engine. Coupon (§7) is left in place and evaluated separately by
/// the pricing service — existing codes keep working through the path they always used — and
/// this covers everything Coupon structurally cannot: automatic no-code discounts, BOGO,
/// free shipping, product/category scoping, customer-group targeting and per-customer caps.
/// </summary>
public interface IPromotionService
{
    /// <summary>Pure evaluation — decides what applies and for how much, without recording usage.</summary>
    Task<PromotionEvaluation> EvaluateAsync(PromotionContext context, CancellationToken ct = default);

    /// <summary>
    /// Records that the given promotions were actually redeemed. Split from evaluation so a
    /// cart preview never burns a usage, and so the increment is atomic at the one moment it
    /// counts — checkout.
    /// </summary>
    Task RegisterUsageAsync(IReadOnlyList<string> promotionIds, CancellationToken ct = default);

    Task<PagedResult<PromotionResponse>> GetAllAsync(string businessId, PageRequest page, CancellationToken ct = default);

    Task<PromotionResponse> CreateAsync(string tenantId, string businessId, CreatePromotionRequest request, CancellationToken ct = default);

    Task<PromotionResponse> UpdateAsync(string tenantId, string businessId, string promotionId, CreatePromotionRequest request, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string promotionId, CancellationToken ct = default);
}
