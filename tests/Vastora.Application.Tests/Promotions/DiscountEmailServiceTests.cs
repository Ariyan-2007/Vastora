using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Promotions;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Promotions;

public class DiscountEmailServiceTests
{
    private sealed class RecordingNotificationService : INotificationService
    {
        public List<string> RecipientEmails { get; } = [];

        public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default)
        {
            RecipientEmails.Add(message.RecipientEmail);
            return Task.CompletedTask;
        }
    }

    private static AppUser Customer(string id, string email, bool marketingOptIn) => new()
    {
        Id = id,
        BusinessId = "biz-1",
        Email = email,
        FullName = "Test Customer",
        NotificationPreferences = new NotificationPreferences { MarketingEmail = marketingOptIn }
    };

    private static (DiscountEmailService Service, RecordingNotificationService Notifications, FakeMongoRepository<AppUser> Users, FakeMongoRepository<CustomerGroup> Groups) Create()
    {
        var coupons = new FakeMongoRepository<Coupon>();
        coupons.Seed(new Coupon
        {
            BusinessId = "biz-1",
            Code = "VIPONLY",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            IsActive = true,
            Visibility = DiscountVisibility.Hidden
        });

        var promotions = new FakeMongoRepository<Promotion>();
        var groups = new FakeMongoRepository<CustomerGroup>();
        var users = new FakeMongoRepository<AppUser>();
        var businesses = new FakeMongoRepository<Business>();
        businesses.Seed(new Business { Id = "biz-1", Currency = "USD" });
        var notifications = new RecordingNotificationService();

        var service = new DiscountEmailService(coupons, promotions, groups, users, businesses, notifications, new PlatformSettingsStub());
        return (service, notifications, users, groups);
    }

    [Fact]
    public async Task SendAsync_UnionsExplicitCustomers_AndGroupMembers()
    {
        var (service, notifications, users, groups) = Create();
        users.Seed(
            Customer("u1", "u1@test.com", marketingOptIn: true),
            Customer("u2", "u2@test.com", marketingOptIn: true),
            Customer("u3", "u3@test.com", marketingOptIn: true));
        groups.Seed(new CustomerGroup { Id = "g1", TenantId = "t1", BusinessId = "biz-1", CustomerUserIds = ["u2", "u3"] });

        var result = await service.SendAsync("t1", "biz-1",
            new SendDiscountEmailRequest("VIPONLY", ["u1"], "g1"), CancellationToken.None);

        Assert.Equal(3, result.TotalRecipients);
        Assert.Equal(3, result.Sent);
        Assert.Equal(0, result.SkippedOptedOut);
        Assert.Equal(["u1@test.com", "u2@test.com", "u3@test.com"], notifications.RecipientEmails.OrderBy(e => e));
    }

    [Fact]
    public async Task SendAsync_SkipsCustomersWhoOptedOutOfMarketingEmail()
    {
        var (service, notifications, users, _) = Create();
        users.Seed(
            Customer("u1", "u1@test.com", marketingOptIn: true),
            Customer("u2", "u2@test.com", marketingOptIn: false));

        // §9.43: a Hidden code being "email-only discoverable" is not license to email someone
        // who opted out — the same marketing-consent rule every other campaign send respects.
        var result = await service.SendAsync("t1", "biz-1",
            new SendDiscountEmailRequest("VIPONLY", ["u1", "u2"], null), CancellationToken.None);

        Assert.Equal(2, result.TotalRecipients);
        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.SkippedOptedOut);
        Assert.Equal(["u1@test.com"], notifications.RecipientEmails);
    }

    [Fact]
    public async Task SendAsync_ThrowsNotFound_ForAnUnknownCode()
    {
        var (service, _, users, _) = Create();
        users.Seed(Customer("u1", "u1@test.com", marketingOptIn: true));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.SendAsync("t1", "biz-1", new SendDiscountEmailRequest("NOPE", ["u1"], null), CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_ThrowsConflict_WhenNoRecipientsAreNamed()
    {
        var (service, _, _, _) = Create();

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.SendAsync("t1", "biz-1", new SendDiscountEmailRequest("VIPONLY", null, null), CancellationToken.None));
    }
}
