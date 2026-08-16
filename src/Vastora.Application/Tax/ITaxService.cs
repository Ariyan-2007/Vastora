using Vastora.Domain.Entities;

namespace Vastora.Application.Tax;

/// <summary>One taxable line resolved to a rate.</summary>
/// <param name="TaxableAmount">The line's contribution to the tax base, after discounts.</param>
public record TaxLine(string Label, decimal RatePercent, decimal TaxableAmount, decimal TaxAmount);

/// <param name="TotalTax">Sum of the line taxes, rounded once at the end.</param>
/// <param name="EffectiveRatePercent">Blended rate across the whole order, for display and for snapshotting onto the Order.</param>
/// <param name="PricesIncludeTax">Snapshotted from the Business — determines whether TotalTax was extracted from, or added to, the subtotal.</param>
public record TaxQuote(decimal TotalTax, decimal EffectiveRatePercent, bool PricesIncludeTax, IReadOnlyList<TaxLine> Lines)
{
    public static TaxQuote None => new(0m, 0m, false, []);
}

/// <summary>
/// §9.19. Deliberately a per-Business rate with named class overrides, not a jurisdiction engine
/// — building a sales-tax nexus resolver on spec would be inventing requirements. The interface
/// is the seam an Avalara/TaxJar implementation would slot into later.
/// </summary>
public interface ITaxService
{
    /// <summary>
    /// Quotes tax for a set of taxable amounts. <paramref name="lineAmounts"/> are post-discount
    /// line totals keyed by the product's TaxClass; <paramref name="shippingAmount"/> is taxed
    /// only when the Business says shipping is taxable in its jurisdiction.
    /// </summary>
    TaxQuote Quote(Business business, IReadOnlyList<(string TaxClass, decimal Amount)> lineAmounts, decimal shippingAmount);
}
