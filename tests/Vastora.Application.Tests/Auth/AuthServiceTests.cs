using Vastora.Application.Auth;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Auth;

public class AuthServiceTests
{
    private static (AuthService Service, FakeMongoRepository<AppUser> Users, FakeMongoRepository<RefreshToken> RefreshTokens,
        FakeMongoRepository<Business> Businesses, FakeMongoRepository<TenantAccount> Tenants, RecordingNotificationService Notifications, PlatformSettingsStub Settings) Create()
    {
        var users = new FakeMongoRepository<AppUser>();
        var businesses = new FakeMongoRepository<Business>();
        var tenants = new FakeMongoRepository<TenantAccount>();
        var refreshTokens = new FakeMongoRepository<RefreshToken>();
        var passwordResetTokens = new FakeMongoRepository<PasswordResetToken>();
        var emailVerificationTokens = new FakeMongoRepository<EmailVerificationToken>();
        var notifications = new RecordingNotificationService();
        var settings = new PlatformSettingsStub();

        var service = new AuthService(
            users, businesses, tenants, refreshTokens, passwordResetTokens, emailVerificationTokens,
            new PasswordHasherFake(), new AuthTokenIssuerStub(), notifications, settings);

        return (service, users, refreshTokens, businesses, tenants, notifications, settings);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_PlatformSuperAdmin_NoRedirectNoAllowedOriginConfigured_UsesConfiguredPublicBaseUrl()
    {
        var (service, users, _, _, _, notifications, settings) = Create();
        users.Seed(new AppUser { Email = "admin@vastora.dev", Role = UserRole.PlatformSuperAdmin });

        await service.RequestPasswordResetAsync("admin@vastora.dev", null, CancellationToken.None);

        Assert.Contains(settings.PublicBaseUrl, notifications.Last!.HtmlBody);
    }

    /// <summary>
    /// The core bug this test locks in: a configured domain must be the real default, not merely
    /// a validator for a client-supplied value that in practice is often never sent (testing via
    /// Postman/Swagger, a background job, ...). Before this fix, no redirectBaseUrl meant the link
    /// always fell back to the static PublicBaseUrl even when a real domain was configured.
    /// </summary>
    [Fact]
    public async Task RequestPasswordResetAsync_PlatformSuperAdmin_NoRedirectSupplied_UsesAllowedOriginAsDefault()
    {
        var (service, users, _, _, _, notifications, settings) = Create();
        users.Seed(new AppUser { Email = "admin@vastora.dev", Role = UserRole.PlatformSuperAdmin });
        settings.AllowedFrontendOrigins = ["http://localhost:5274"];

        await service.RequestPasswordResetAsync("admin@vastora.dev", null, CancellationToken.None);

        Assert.Contains("http://localhost:5274/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_PlatformSuperAdmin_RedirectMatchesAllowedOrigin_UsesRedirectInstead()
    {
        var (service, users, _, _, _, notifications, settings) = Create();
        users.Seed(new AppUser { Email = "admin@vastora.dev", Role = UserRole.PlatformSuperAdmin });
        settings.AllowedFrontendOrigins = ["http://localhost:5173"];

        await service.RequestPasswordResetAsync("admin@vastora.dev", "http://localhost:5173", CancellationToken.None);

        Assert.Contains("http://localhost:5173/reset-password", notifications.Last!.HtmlBody);
    }

    /// <summary>
    /// The exact attack ResolveLinkBase exists to stop: an unvalidated redirect would let anyone
    /// email a real reset token to a victim's inbox pointing at a domain the attacker controls.
    /// Falls back to the configured origin, not the generic PublicBaseUrl — that origin is still
    /// legitimate, just not the one this particular request claimed.
    /// </summary>
    [Fact]
    public async Task RequestPasswordResetAsync_PlatformSuperAdmin_RedirectNotOnAllowlist_FallsBackToAllowedOrigin()
    {
        var (service, users, _, _, _, notifications, settings) = Create();
        users.Seed(new AppUser { Email = "admin@vastora.dev", Role = UserRole.PlatformSuperAdmin });
        settings.AllowedFrontendOrigins = ["http://localhost:5173"];

        await service.RequestPasswordResetAsync("admin@vastora.dev", "https://attacker.example", CancellationToken.None);

        Assert.Contains("http://localhost:5173/reset-password", notifications.Last!.HtmlBody);
        Assert.DoesNotContain("attacker.example", notifications.Last!.HtmlBody);
    }

    /// <summary>TenantOwner's trust anchor is dynamic — TenantAccount.SuperOfficeDomain, set by Platform Admin, not static config.</summary>
    [Fact]
    public async Task RequestPasswordResetAsync_TenantOwner_RedirectMatchesSuperOfficeDomain_UsesRedirectInstead()
    {
        var (service, users, _, _, tenants, notifications, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Slug = "antivaly-hq", SuperOfficeDomain = "office.antivaly.com" })[0];
        users.Seed(new AppUser { Email = "owner@antivaly.com", TenantId = tenant.Id, Role = UserRole.TenantOwner });

        await service.RequestPasswordResetAsync("owner@antivaly.com", "https://office.antivaly.com", CancellationToken.None);

        Assert.Contains("https://office.antivaly.com/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_TenantOwner_NoRedirectSupplied_UsesSuperOfficeDomainAsDefault()
    {
        var (service, users, _, _, tenants, notifications, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Slug = "antivaly-hq", SuperOfficeDomain = "office.antivaly.com" })[0];
        users.Seed(new AppUser { Email = "owner@antivaly.com", TenantId = tenant.Id, Role = UserRole.TenantOwner });

        await service.RequestPasswordResetAsync("owner@antivaly.com", null, CancellationToken.None);

        Assert.Contains("https://office.antivaly.com/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_TenantOwner_RedirectDoesNotMatchSuperOfficeDomain_FallsBackToSuperOfficeDomain()
    {
        var (service, users, _, _, tenants, notifications, _) = Create();
        var tenant = tenants.Seed(new TenantAccount { Slug = "antivaly-hq", SuperOfficeDomain = "office.antivaly.com" })[0];
        users.Seed(new AppUser { Email = "owner@antivaly.com", TenantId = tenant.Id, Role = UserRole.TenantOwner });

        await service.RequestPasswordResetAsync("owner@antivaly.com", "https://evil.example", CancellationToken.None);

        Assert.Contains("https://office.antivaly.com/reset-password", notifications.Last!.HtmlBody);
        Assert.DoesNotContain("evil.example", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_TenantOwner_NoSuperOfficeDomainConfigured_FallsBackToConfiguredPublicBaseUrl()
    {
        var (service, users, _, _, tenants, notifications, settings) = Create();
        var tenant = tenants.Seed(new TenantAccount { Slug = "antivaly-hq" })[0];
        users.Seed(new AppUser { Email = "owner@antivaly.com", TenantId = tenant.Id, Role = UserRole.TenantOwner });

        await service.RequestPasswordResetAsync("owner@antivaly.com", null, CancellationToken.None);

        Assert.Contains(settings.PublicBaseUrl, notifications.Last!.HtmlBody);
    }

    /// <summary>Business staff's trust anchor is Business.BackOfficeDomain, set by that Business's own SuperOffice — never the customer-facing ShopDomain.</summary>
    [Fact]
    public async Task RequestPasswordResetAsync_BusinessAdmin_RedirectMatchesBackOfficeDomain_UsesRedirectInstead()
    {
        var (service, users, _, businesses, _, notifications, _) = Create();
        var business = businesses.Seed(new Business { Slug = "antivaly", ShopDomain = "shop.antivaly.com", BackOfficeDomain = "staff.antivaly.com", Status = BusinessStatus.Active })[0];
        users.Seed(new AppUser { Email = "staff@antivaly.com", BusinessId = business.Id, Role = UserRole.BusinessAdmin });

        await service.RequestPasswordResetAsync("staff@antivaly.com", "https://staff.antivaly.com", CancellationToken.None);

        Assert.Contains("https://staff.antivaly.com/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_BusinessAdmin_NoRedirectSupplied_UsesBackOfficeDomainAsDefault()
    {
        var (service, users, _, businesses, _, notifications, _) = Create();
        var business = businesses.Seed(new Business { Slug = "antivaly", ShopDomain = "shop.antivaly.com", BackOfficeDomain = "staff.antivaly.com", Status = BusinessStatus.Active })[0];
        users.Seed(new AppUser { Email = "staff@antivaly.com", BusinessId = business.Id, Role = UserRole.BusinessAdmin });

        await service.RequestPasswordResetAsync("staff@antivaly.com", null, CancellationToken.None);

        Assert.Contains("https://staff.antivaly.com/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_BusinessAdmin_RedirectMatchesShopDomainNotBackOfficeDomain_FallsBackToBackOfficeDomain()
    {
        var (service, users, _, businesses, _, notifications, _) = Create();
        var business = businesses.Seed(new Business { Slug = "antivaly", ShopDomain = "shop.antivaly.com", BackOfficeDomain = "staff.antivaly.com", Status = BusinessStatus.Active })[0];
        users.Seed(new AppUser { Email = "staff@antivaly.com", BusinessId = business.Id, Role = UserRole.BusinessAdmin });

        // A Business's customer-facing ShopDomain must not double as its staff BackOfficeDomain.
        await service.RequestPasswordResetAsync("staff@antivaly.com", "https://shop.antivaly.com", CancellationToken.None);

        Assert.Contains("https://staff.antivaly.com/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestStorefrontPasswordResetAsync_RedirectMatchesBusinessShopDomain_UsesRedirectInstead()
    {
        var (service, users, _, businesses, _, notifications, _) = Create();
        var business = businesses.Seed(new Business { Slug = "antivaly", ShopDomain = "shop.antivaly.com", Status = BusinessStatus.Active })[0];
        users.Seed(new AppUser { Email = "customer@example.com", BusinessId = business.Id, Role = UserRole.Customer });

        await service.RequestStorefrontPasswordResetAsync("antivaly", "customer@example.com", "https://shop.antivaly.com", CancellationToken.None);

        Assert.Contains("https://shop.antivaly.com/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestStorefrontPasswordResetAsync_NoRedirectSupplied_UsesShopDomainAsDefault()
    {
        var (service, users, _, businesses, _, notifications, _) = Create();
        var business = businesses.Seed(new Business { Slug = "antivaly", ShopDomain = "shop.antivaly.com", Status = BusinessStatus.Active })[0];
        users.Seed(new AppUser { Email = "customer@example.com", BusinessId = business.Id, Role = UserRole.Customer });

        await service.RequestStorefrontPasswordResetAsync("antivaly", "customer@example.com", null, CancellationToken.None);

        Assert.Contains("https://shop.antivaly.com/reset-password", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestStorefrontPasswordResetAsync_RedirectDoesNotMatchShopDomain_FallsBackToShopDomain()
    {
        var (service, users, _, businesses, _, notifications, _) = Create();
        var business = businesses.Seed(new Business { Slug = "antivaly", ShopDomain = "shop.antivaly.com", Status = BusinessStatus.Active })[0];
        users.Seed(new AppUser { Email = "customer@example.com", BusinessId = business.Id, Role = UserRole.Customer });

        await service.RequestStorefrontPasswordResetAsync("antivaly", "customer@example.com", "https://evil.example", CancellationToken.None);

        Assert.Contains("https://shop.antivaly.com/reset-password", notifications.Last!.HtmlBody);
        Assert.DoesNotContain("evil.example", notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task RequestStorefrontPasswordResetAsync_NoShopDomainConfigured_FallsBackToConfiguredPublicBaseUrl()
    {
        var (service, users, _, businesses, _, notifications, settings) = Create();
        var business = businesses.Seed(new Business { Slug = "antivaly", Status = BusinessStatus.Active })[0];
        users.Seed(new AppUser { Email = "customer@example.com", BusinessId = business.Id, Role = UserRole.Customer });

        await service.RequestStorefrontPasswordResetAsync("antivaly", "customer@example.com", null, CancellationToken.None);

        Assert.Contains(settings.PublicBaseUrl, notifications.Last!.HtmlBody);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsUnauthorized()
    {
        var (service, users, _, _, _, _, _) = Create();
        var user = users.Seed(new AppUser { PasswordHash = "hashed:old-password" })[^1];

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            service.ChangePasswordAsync(user.Id, "wrong-password", "new-password", CancellationToken.None));
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectCurrentPassword_UpdatesHashAndRevokesActiveSessions()
    {
        var (service, users, refreshTokens, _, _, _, _) = Create();
        var user = users.Seed(new AppUser { PasswordHash = "hashed:old-password" })[^1];
        var session = refreshTokens.Seed(new RefreshToken { UserId = user.Id })[^1];

        await service.ChangePasswordAsync(user.Id, "old-password", "new-password", CancellationToken.None);

        Assert.Equal("hashed:new-password", user.PasswordHash);
        var reloaded = (await refreshTokens.FindAsync(t => t.Id == session.Id, CancellationToken.None))[0];
        Assert.NotNull(reloaded.RevokedAt);
    }

    private sealed class PasswordHasherFake : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string hash) => hash == $"hashed:{password}";
    }

    private sealed class AuthTokenIssuerStub : IAuthTokenIssuer
    {
        public Task<AuthResponse> IssueAsync(AppUser user, string createdByIp, CancellationToken ct = default) =>
            throw new NotImplementedException("Not exercised by ChangePasswordAsync.");
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public NotificationMessage? Last { get; private set; }

        public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default)
        {
            Last = message;
            return Task.CompletedTask;
        }
    }
}
