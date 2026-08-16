using Vastora.Application.Tax;
using Vastora.Domain.Entities;

namespace Vastora.Application.Tests.Tax;

public class TaxServiceTests
{
    private static Business BusinessWithTax(decimal rate, bool inclusive, bool taxShipping = false) => new()
    {
        Tax = new TaxSettings
        {
            Enabled = true,
            DefaultRatePercent = rate,
            PricesIncludeTax = inclusive,
            TaxShipping = taxShipping,
            DisplayName = "VAT"
        }
    };

    [Fact]
    public void Quote_ReturnsNothing_WhenTaxIsDisabled()
    {
        var result = new TaxService().Quote(new Business(), [(string.Empty, 100m)], 0m);

        Assert.Equal(0m, result.TotalTax);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Quote_AddsTaxOnTop_WhenPricesAreExclusive()
    {
        var result = new TaxService().Quote(BusinessWithTax(15m, inclusive: false), [(string.Empty, 200m)], 0m);

        Assert.Equal(30m, result.TotalTax);
        Assert.False(result.PricesIncludeTax);
    }

    [Fact]
    public void Quote_ExtractsTax_WhenPricesAlreadyIncludeIt()
    {
        var result = new TaxService().Quote(BusinessWithTax(15m, inclusive: true), [(string.Empty, 230m)], 0m);

        // 230 gross at 15% contains 30 of tax, not 34.50. Adding rather than extracting would
        // overcharge every customer of every VAT-convention business.
        Assert.Equal(30m, result.TotalTax);
        Assert.True(result.PricesIncludeTax);
    }

    [Fact]
    public void Quote_UsesAPerClassRate_WhenTheProductNamesOne()
    {
        var business = BusinessWithTax(20m, inclusive: false);
        business.Tax.ClassRates["reduced"] = 5m;

        var result = new TaxService().Quote(business, [("reduced", 100m), (string.Empty, 100m)], 0m);

        // 5 on the reduced-rate line, 20 on the standard one.
        Assert.Equal(25m, result.TotalTax);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public void Quote_TaxesShipping_OnlyWhenTheBusinessSaysItsJurisdictionDoes()
    {
        var untaxed = new TaxService().Quote(BusinessWithTax(10m, false, taxShipping: false), [(string.Empty, 100m)], 50m);
        var taxed = new TaxService().Quote(BusinessWithTax(10m, false, taxShipping: true), [(string.Empty, 100m)], 50m);

        Assert.Equal(10m, untaxed.TotalTax);
        Assert.Equal(15m, taxed.TotalTax);
    }
}
