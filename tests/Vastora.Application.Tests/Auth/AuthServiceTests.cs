using Vastora.Application.Auth;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;

namespace Vastora.Application.Tests.Auth;

public class AuthServiceTests
{
    private static (AuthService Service, FakeMongoRepository<AppUser> Users, FakeMongoRepository<RefreshToken> RefreshTokens) Create()
    {
        var users = new FakeMongoRepository<AppUser>();
        var businesses = new FakeMongoRepository<Business>();
        var refreshTokens = new FakeMongoRepository<RefreshToken>();
        var passwordResetTokens = new FakeMongoRepository<PasswordResetToken>();
        var emailVerificationTokens = new FakeMongoRepository<EmailVerificationToken>();

        var service = new AuthService(
            users, businesses, refreshTokens, passwordResetTokens, emailVerificationTokens,
            new PasswordHasherFake(), new AuthTokenIssuerStub(), new NotificationServiceStub(), new PlatformSettingsStub());

        return (service, users, refreshTokens);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsUnauthorized()
    {
        var (service, users, _) = Create();
        var user = users.Seed(new AppUser { PasswordHash = "hashed:old-password" })[^1];

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            service.ChangePasswordAsync(user.Id, "wrong-password", "new-password", CancellationToken.None));
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectCurrentPassword_UpdatesHashAndRevokesActiveSessions()
    {
        var (service, users, refreshTokens) = Create();
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

    private sealed class NotificationServiceStub : INotificationService
    {
        public Task NotifyAsync(NotificationMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PlatformSettingsStub : IPlatformSettings
    {
        public bool RequireEmailVerification => false;
        public TimeSpan AbandonedCartAfter => TimeSpan.FromHours(4);
        public string PublicBaseUrl => "https://example.test";
    }
}
