using Vastora.Domain.Entities;

namespace Vastora.Application.Tax;

/// <inheritdoc cref="ITaxService"/>
public class TaxService : ITaxService
{
    public TaxQuote Quote(Business business, IReadOnlyList<(string TaxClass, decimal Amount)> lineAmounts, decimal shippingAmount)
    {
        var settings = business.Tax;
        if (!settings.Enabled)
        {
            return TaxQuote.None;
        }

        var lines = new List<TaxLine>();

        foreach (var group in lineAmounts.GroupBy(l => l.TaxClass ?? string.Empty))
        {
            var rate = RateFor(business, group.Key);
            if (rate <= 0)
            {
                continue;
            }

            var taxable = group.Sum(l => l.Amount);
            lines.Add(BuildLine(LabelFor(business, group.Key), rate, taxable, settings.PricesIncludeTax));
        }

        if (settings.TaxShipping && shippingAmount > 0 && settings.DefaultRatePercent > 0)
        {
            lines.Add(BuildLine(
                $"{settings.DisplayName} (shipping)",
                settings.DefaultRatePercent,
                shippingAmount,
                settings.PricesIncludeTax));
        }

        var totalTax = Math.Round(lines.Sum(l => l.TaxAmount), 2, MidpointRounding.AwayFromZero);
        var totalTaxable = lines.Sum(l => l.TaxableAmount);
        var effectiveRate = totalTaxable <= 0
            ? 0m
            : Math.Round(totalTax / totalTaxable * 100m, 4, MidpointRounding.AwayFromZero);

        return new TaxQuote(totalTax, effectiveRate, settings.PricesIncludeTax, lines);
    }

    /// <summary>
    /// The tax-inclusive branch is the one worth reading twice. When catalog prices already
    /// contain tax (the VAT convention), the tax is *extracted* from the amount —
    /// <c>amount − amount / (1 + rate)</c> — not added on top. Adding it on top instead would
    /// overcharge every customer of every VAT-convention business, which is why the flag is also
    /// snapshotted onto each Order rather than only read live from the Business.
    /// </summary>
    private static TaxLine BuildLine(string label, decimal ratePercent, decimal taxableAmount, bool pricesIncludeTax)
    {
        var tax = ExtractTaxCore(taxableAmount, ratePercent, pricesIncludeTax);

        return new TaxLine(label, ratePercent, Math.Round(taxableAmount, 2, MidpointRounding.AwayFromZero),
            Math.Round(tax, 2, MidpointRounding.AwayFromZero));
    }

    public decimal ExtractTax(decimal amount, decimal ratePercent, bool pricesIncludeTax) =>
        amount <= 0 || ratePercent <= 0
            ? 0m
            : Math.Round(ExtractTaxCore(amount, ratePercent, pricesIncludeTax), 2, MidpointRounding.AwayFromZero);

    private static decimal ExtractTaxCore(decimal amount, decimal ratePercent, bool pricesIncludeTax)
    {
        var rate = ratePercent / 100m;

        return pricesIncludeTax
            ? amount - amount / (1 + rate)
            : amount * rate;
    }

    private static decimal RateFor(Business business, string taxClass) =>
        !string.IsNullOrWhiteSpace(taxClass) && business.Tax.ClassRates.TryGetValue(taxClass, out var classRate)
            ? classRate
            : business.Tax.DefaultRatePercent;

    private static string LabelFor(Business business, string taxClass) =>
        string.IsNullOrWhiteSpace(taxClass)
            ? business.Tax.DisplayName
            : $"{business.Tax.DisplayName} ({taxClass})";
}
