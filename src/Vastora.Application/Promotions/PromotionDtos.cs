using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Promotions;

/// <summary>One promotion that fired, and what it took off. Surfaced to the storefront so a
/// customer can see *why* their total dropped rather than just that it did.</summary>
public record AppliedDiscount(string Source, string Label, decimal Amount, bool IsFreeShipping);

public record CreatePromotionRequest(
    string Name,
    string? Code,
    PromotionEffect Effect,
    PromotionScope Scope,
    decimal Value,
    List<string>? ProductIds,
    List<string>? CategoryIds,
    int BuyQuantity,
    int GetQuantity,
    decimal? MinOrderAmount,
    List<string>? CustomerGroupIds,
    bool FirstOrderOnly,
    int? MaxUses,
    int? MaxUsesPerCustomer,
    int Priority,
    bool Stackable,
    DateTime StartsAt,
    DateTime? EndsAt,
    bool IsActive,
    /// <summary>§9.43. Hidden means the promotion never appears in the storefront's
    /// available-offers listing — it still applies when its <c>Code</c> is entered exactly.
    /// Meaningless for an automatic (no-code) promotion.</summary>
    DiscountVisibility Visibility = DiscountVisibility.Public);

public record PromotionResponse(
    string Id,
    string Name,
    string? Code,
    PromotionEffect Effect,
    PromotionScope Scope,
    decimal Value,
    List<string> ProductIds,
    List<string> CategoryIds,
    int BuyQuantity,
    int GetQuantity,
    decimal? MinOrderAmount,
    List<string> CustomerGroupIds,
    bool FirstOrderOnly,
    int? MaxUses,
    int UsedCount,
    int? MaxUsesPerCustomer,
    int Priority,
    bool Stackable,
    DateTime StartsAt,
    DateTime? EndsAt,
    bool IsActive,
    bool IsLiveNow,
    DiscountVisibility Visibility);
