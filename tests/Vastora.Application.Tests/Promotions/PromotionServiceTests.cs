using Vastora.Application.Promotions;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Promotions;

public class PromotionServiceTests
{
    private static (PromotionService Service, FakeMongoRepository<Promotion> Promotions, FakeMongoRepository<Order> Orders) Create()
    {
        var promotions = new FakeMongoRepository<Promotion>();
        var orders = new FakeMongoRepository<Order>();
        return (new PromotionService(promotions, orders), promotions, orders);
    }

    private static PromotionContext Context(params PricedLine[] lines) =>
        new("biz-1", "cust-1", [], true, lines, []);

    private static Promotion Live(Action<Promotion> configure)
    {
        var promotion = new Promotion
        {
            BusinessId = "biz-1",
            Name = "Test promotion",
            IsActive = true,
            Stackable = true,
            StartsAt = DateTime.UtcNow.AddDays(-1)
        };

        configure(promotion);
        return promotion;
    }

    [Fact]
    public async Task EvaluateAsync_AppliesAnAutomaticPromotion_WithNoCodeEntered()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(Live(p =>
        {
            p.Effect = PromotionEffect.PercentageOff;
            p.Value = 10m;
        }));

        var result = await service.EvaluateAsync(Context(new PricedLine("p1", "c1", 100m, 2)), CancellationToken.None);

        // No code typed and it still fires — the thing Coupon structurally could not do.
        Assert.Equal(20m, result.TotalDiscount);
    }

    [Fact]
    public async Task EvaluateAsync_SkipsACodedPromotion_UntilItsCodeIsEntered()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(Live(p =>
        {
            p.Code = "SAVE10";
            p.Effect = PromotionEffect.PercentageOff;
            p.Value = 10m;
        }));

        var withoutCode = await service.EvaluateAsync(Context(new PricedLine("p1", "c1", 100m, 1)), CancellationToken.None);
        var withCode = await service.EvaluateAsync(
            Context(new PricedLine("p1", "c1", 100m, 1)) with { EnteredCodes = ["save10"] }, CancellationToken.None);

        Assert.Equal(0m, withoutCode.TotalDiscount);
        Assert.Equal(10m, withCode.TotalDiscount); // matching is case-insensitive
    }

    [Fact]
    public async Task EvaluateAsync_NeverDiscountsMoreThanTheBasketIsWorth()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(Live(p =>
        {
            p.Effect = PromotionEffect.FixedAmountOff;
            p.Value = 50m;
        }));

        var result = await service.EvaluateAsync(Context(new PricedLine("p1", "c1", 30m, 1)), CancellationToken.None);

        // A $50-off code on a $30 basket must not produce a negative total.
        Assert.Equal(30m, result.TotalDiscount);
    }

    [Fact]
    public async Task EvaluateAsync_BuyXGetY_DiscountsTheCheapestQualifyingUnits()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(Live(p =>
        {
            p.Effect = PromotionEffect.BuyXGetY;
            p.BuyQuantity = 2;
            p.GetQuantity = 1;
        }));

        var result = await service.EvaluateAsync(
            Context(new PricedLine("p1", "c1", 30m, 1), new PricedLine("p2", "c1", 10m, 2)),
            CancellationToken.None);

        // Three units, one group of three, the cheapest unit is free — the convention that can't
        // be gamed by adding an expensive item to a cart of cheap ones.
        Assert.Equal(10m, result.TotalDiscount);
    }

    [Fact]
    public async Task EvaluateAsync_ScopesToNamedCategories_WhenScopeIsCategories()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(Live(p =>
        {
            p.Scope = PromotionScope.Categories;
            p.CategoryIds = ["shoes"];
            p.Effect = PromotionEffect.PercentageOff;
            p.Value = 50m;
        }));

        var result = await service.EvaluateAsync(
            Context(new PricedLine("p1", "shoes", 100m, 1), new PricedLine("p2", "hats", 100m, 1)),
            CancellationToken.None);

        Assert.Equal(50m, result.TotalDiscount);
    }

    [Fact]
    public async Task EvaluateAsync_ANonStackablePromotionSuppressesEveryLowerPriorityOne()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(
            Live(p =>
            {
                p.Name = "Exclusive";
                p.Priority = 1;
                p.Stackable = false;
                p.Effect = PromotionEffect.PercentageOff;
                p.Value = 10m;
            }),
            Live(p =>
            {
                p.Name = "Also 10%";
                p.Priority = 2;
                p.Effect = PromotionEffect.PercentageOff;
                p.Value = 10m;
            }));

        var result = await service.EvaluateAsync(Context(new PricedLine("p1", "c1", 100m, 1)), CancellationToken.None);

        Assert.Single(result.Discounts);
        Assert.Equal("Exclusive", result.Discounts[0].Label);
    }

    [Fact]
    public async Task EvaluateAsync_IgnoresAnExpiredOrExhaustedPromotion()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(
            Live(p =>
            {
                p.Name = "Expired";
                p.EndsAt = DateTime.UtcNow.AddDays(-1);
                p.Effect = PromotionEffect.PercentageOff;
                p.Value = 10m;
            }),
            Live(p =>
            {
                p.Name = "Exhausted";
                p.MaxUses = 5;
                p.UsedCount = 5;
                p.Effect = PromotionEffect.PercentageOff;
                p.Value = 10m;
            }));

        var result = await service.EvaluateAsync(Context(new PricedLine("p1", "c1", 100m, 1)), CancellationToken.None);

        Assert.Equal(0m, result.TotalDiscount);
    }

    [Fact]
    public async Task RegisterUsageAsync_StopsAtTheCap()
    {
        var (service, promotions, _) = Create();
        var promotion = promotions.Seed(Live(p =>
        {
            p.MaxUses = 2;
            p.Effect = PromotionEffect.PercentageOff;
            p.Value = 10m;
        }))[0];

        await service.RegisterUsageAsync([promotion.Id], CancellationToken.None);
        await service.RegisterUsageAsync([promotion.Id], CancellationToken.None);
        await service.RegisterUsageAsync([promotion.Id], CancellationToken.None);

        // The capped increment is atomic in the real repository; here the contract under test is
        // that it refuses past the cap rather than counting to three.
        Assert.Equal(2, (await promotions.GetByIdAsync(promotion.Id, CancellationToken.None))!.UsedCount);
    }

    [Fact]
    public async Task GetPublicLiveAsync_ExcludesHiddenAndAutomaticPromotions()
    {
        var (service, promotions, _) = Create();
        promotions.Seed(
            Live(p => { p.Code = "SUMMER10"; p.Visibility = DiscountVisibility.Public; p.Effect = PromotionEffect.PercentageOff; p.Value = 10m; }),
            Live(p => { p.Code = "VIPSECRET"; p.Visibility = DiscountVisibility.Hidden; p.Effect = PromotionEffect.PercentageOff; p.Value = 30m; }),
            Live(p => { p.Code = null; p.Visibility = DiscountVisibility.Public; p.Effect = PromotionEffect.PercentageOff; p.Value = 5m; }));

        // §9.43: Hidden never appears (targeted/email-only campaign case), and an automatic
        // no-code promotion has nothing for a shopper to type, so it's excluded too.
        var listed = await service.GetPublicLiveAsync("biz-1", CancellationToken.None);

        var promo = Assert.Single(listed);
        Assert.Equal("SUMMER10", promo.Code);
    }
}
