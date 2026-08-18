using Vastora.Application.GiftCards;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.GiftCards;

public class StoreCreditServiceTests
{
    private static (StoreCreditService Service, FakeMongoRepository<StoreCreditEntry> Entries) Create()
    {
        var entries = new FakeMongoRepository<StoreCreditEntry>();
        var businesses = new FakeMongoRepository<Business>();
        businesses.Seed(new Business { Id = "biz-1", Currency = "USD" });
        return (new StoreCreditService(entries, businesses), entries);
    }

    [Fact]
    public async Task GetBalanceAsync_ExcludesAnExpiredPromotionalCredit()
    {
        var (service, _) = Create();

        // A permanent refund-settlement credit (no ExpiresAt) plus a promotional grant that has
        // already lapsed. §9.43: the balance must live-filter the expired credit out, the same
        // way Coupon.IsValidNow and GiftCard.IsRedeemableNow are computed live, with no sweep job.
        await service.RecordAsync("t1", "biz-1", "cust-1", 30m, StoreCreditReason.RefundToCredit, "refund", ct: CancellationToken.None);
        await service.RecordAsync("t1", "biz-1", "cust-1", 15m, StoreCreditReason.LoyaltyReward, "promo",
            expiresAt: DateTime.UtcNow.AddDays(-1), ct: CancellationToken.None);

        var balance = await service.GetBalanceAsync("biz-1", "cust-1", CancellationToken.None);

        Assert.Equal(30m, balance);
    }

    [Fact]
    public async Task GetBalanceAsync_IncludesAPromotionalCredit_UntilItExpires()
    {
        var (service, _) = Create();

        await service.RecordAsync("t1", "biz-1", "cust-1", 15m, StoreCreditReason.LoyaltyReward, "promo",
            expiresAt: DateTime.UtcNow.AddDays(1), ct: CancellationToken.None);

        var balance = await service.GetBalanceAsync("biz-1", "cust-1", CancellationToken.None);

        Assert.Equal(15m, balance);
    }

    [Fact]
    public async Task GetBalanceAsync_NeverExpiresASpend_EvenIfItWereSomehowFlagged()
    {
        var (service, _) = Create();

        await service.RecordAsync("t1", "biz-1", "cust-1", 50m, StoreCreditReason.RefundToCredit, "refund", ct: CancellationToken.None);
        var spent = await service.SpendAsync("t1", "biz-1", "cust-1", 20m, "order-1", CancellationToken.None);

        Assert.Equal(20m, spent);
        Assert.Equal(30m, await service.GetBalanceAsync("biz-1", "cust-1", CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_IgnoresExpiresAt_OnADebit()
    {
        var (service, entries) = Create();

        // A spend (negative amount) passing expiresAt would be a caller bug — the field is only
        // meaningful on a credit, so it must be dropped rather than silently expiring a debit.
        await service.RecordAsync("t1", "biz-1", "cust-1", -5m, StoreCreditReason.Spent, "note",
            expiresAt: DateTime.UtcNow.AddDays(1), ct: CancellationToken.None);

        var entry = Assert.Single(await entries.FindAsync(e => e.BusinessId == "biz-1", CancellationToken.None));
        Assert.Null(entry.ExpiresAt);
    }
}
