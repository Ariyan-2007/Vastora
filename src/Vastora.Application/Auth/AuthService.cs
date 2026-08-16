using System.Security.Cryptography;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Auth;

public class AuthService(
    IMongoRepository<AppUser> users,
    IMongoRepository<Business> businesses,
    IMongoRepository<RefreshToken> refreshTokens,
    IMongoRepository<PasswordResetToken> passwordResetTokens,
    IMongoRepository<EmailVerificationToken> emailVerificationTokens,
    IPasswordHasher passwordHasher,
    IAuthTokenIssuer tokenIssuer,
    INotificationService notificationService,
    IPlatformSettings platformSettings) : IAuthService
{
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromHours(1);

    private static readonly TimeSpan EmailVerificationTokenLifetime = TimeSpan.FromDays(3);

    /// <inheritdoc cref="IPlatformSettings.RequireEmailVerification"/>
    private bool RequireEmailVerification => platformSettings.RequireEmailVerification;

    public async Task<AuthResponse> BackOfficeLoginAsync(BackOfficeLoginRequest request, string ip, CancellationToken ct = default)
    {
        var user = await users.FindOneAsync(u => u.Email == request.Email && u.Role != UserRole.Customer, ct)
            ?? throw new UnauthorizedAppException();

        return await AuthenticateAsync(user, request.Password, ip, ct);
    }

    public async Task<AuthResponse> StorefrontLoginAsync(string businessSlug, StorefrontLoginRequest request, string ip, CancellationToken ct = default)
    {
        var business = await ResolveBusinessAsync(businessSlug, ct);

        var user = await users.FindOneAsync(
            u => u.Email == request.Email && u.BusinessId == business.Id && u.Role == UserRole.Customer, ct)
            ?? throw new UnauthorizedAppException();

        return await AuthenticateAsync(user, request.Password, ip, ct);
    }

    public async Task<AuthResponse> StorefrontRegisterAsync(string businessSlug, StorefrontRegisterRequest request, string ip, CancellationToken ct = default)
    {
        var business = await ResolveBusinessAsync(businessSlug, ct);

        var emailTaken = await users.ExistsAsync(
            u => u.Email == request.Email && u.BusinessId == business.Id, ct);
        if (emailTaken)
        {
            throw new ConflictException($"An account with email '{request.Email}' already exists for this shop.");
        }

        var user = new AppUser
        {
            TenantId = business.TenantId,
            BusinessId = business.Id,
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = UserRole.Customer,
            // The enum's own default finally becomes reachable — before §9.34 every account was
            // created Active with an unproven email address, including the one the password-reset
            // flow trusts.
            Status = RequireEmailVerification ? UserStatus.PendingVerification : UserStatus.Active,
            UnsubscribeToken = GenerateUnsubscribeToken()
        };

        await users.AddAsync(user, ct);
        await IssueEmailVerificationAsync(user, ct);

        if (RequireEmailVerification)
        {
            throw new ForbiddenException("Check your inbox — confirm your email address to finish creating your account.");
        }

        return await tokenIssuer.IssueAsync(user, ip, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string ip, CancellationToken ct = default)
    {
        var tokenHash = AuthTokenIssuer.Hash(request.RefreshToken);
        var stored = await refreshTokens.FindOneAsync(t => t.TokenHash == tokenHash, ct);

        if (stored is null || !stored.IsActive)
        {
            throw new UnauthorizedAppException("Refresh token is invalid or has expired.");
        }

        var user = await users.GetByIdAsync(stored.UserId, ct)
            ?? throw new UnauthorizedAppException();

        stored.RevokedAt = DateTime.UtcNow;
        await refreshTokens.UpdateAsync(stored, ct);

        return await tokenIssuer.IssueAsync(user, ip, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = AuthTokenIssuer.Hash(refreshToken);
        var stored = await refreshTokens.FindOneAsync(t => t.TokenHash == tokenHash, ct);
        if (stored is null || stored.RevokedAt is not null)
        {
            return;
        }

        stored.RevokedAt = DateTime.UtcNow;
        await refreshTokens.UpdateAsync(stored, ct);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var user = await users.FindOneAsync(u => u.Email == email && u.Role != UserRole.Customer, ct);
        if (user is not null)
        {
            await IssuePasswordResetAsync(user, ct);
        }
    }

    public async Task RequestStorefrontPasswordResetAsync(string businessSlug, string email, CancellationToken ct = default)
    {
        Business? business;
        try
        {
            business = await ResolveBusinessAsync(businessSlug, ct);
        }
        catch (AppException)
        {
            // Deliberately silent — same non-enumeration reasoning as an unknown email below.
            return;
        }

        var user = await users.FindOneAsync(
            u => u.Email == email && u.BusinessId == business.Id && u.Role == UserRole.Customer, ct);
        if (user is not null)
        {
            await IssuePasswordResetAsync(user, ct);
        }
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        var tokenHash = AuthTokenIssuer.Hash(token);
        var stored = await passwordResetTokens.FindOneAsync(t => t.TokenHash == tokenHash, ct);
        if (stored is null || !stored.IsActive)
        {
            throw new UnauthorizedAppException("This password reset link is invalid or has expired.");
        }

        var user = await users.GetByIdAsync(stored.UserId, ct)
            ?? throw new UnauthorizedAppException();

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await users.UpdateAsync(user, ct);

        stored.UsedAt = DateTime.UtcNow;
        await passwordResetTokens.UpdateAsync(stored, ct);

        // A reset is a "something may be compromised" signal — sign the user out everywhere.
        var activeSessions = await refreshTokens.FindAsync(t => t.UserId == user.Id && t.RevokedAt == null, ct);
        foreach (var session in activeSessions)
        {
            session.RevokedAt = DateTime.UtcNow;
            await refreshTokens.UpdateAsync(session, ct);
        }
    }

    public async Task RequestEmailVerificationAsync(string userId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (user.IsEmailVerified)
        {
            return;
        }

        await IssueEmailVerificationAsync(user, ct);
    }

    public async Task VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = AuthTokenIssuer.Hash(token);
        var stored = await emailVerificationTokens.FindOneAsync(t => t.TokenHash == tokenHash, ct);

        if (stored is null || !stored.IsActive(DateTime.UtcNow))
        {
            throw new UnauthorizedAppException("This verification link is invalid or has expired.");
        }

        var user = await users.GetByIdAsync(stored.UserId, ct)
            ?? throw new UnauthorizedAppException();

        // The address is re-checked against the one captured at issue time: if the user changed
        // their email after requesting verification, this token proves nothing about the new one.
        if (!string.Equals(user.Email, stored.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAppException("This verification link was issued for a different email address.");
        }

        user.EmailVerifiedAt = DateTime.UtcNow;
        if (user.Status == UserStatus.PendingVerification)
        {
            user.Status = UserStatus.Active;
        }

        await users.UpdateAsync(user, ct);

        stored.UsedAt = DateTime.UtcNow;
        await emailVerificationTokens.UpdateAsync(stored, ct);
    }

    private async Task IssueEmailVerificationAsync(AppUser user, CancellationToken ct)
    {
        // Any previously issued token is retired first, so a resend genuinely replaces the old
        // link rather than leaving several live at once.
        var outstanding = await emailVerificationTokens.FindAsync(
            t => t.UserId == user.Id && t.UsedAt == null, ct);

        foreach (var old in outstanding)
        {
            old.UsedAt = DateTime.UtcNow;
            await emailVerificationTokens.UpdateAsync(old, ct);
        }

        var tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.Add(EmailVerificationTokenLifetime);

        await emailVerificationTokens.AddAsync(new EmailVerificationToken
        {
            UserId = user.Id,
            Email = user.Email,
            TokenHash = AuthTokenIssuer.Hash(tokenValue),
            ExpiresAt = expiresAt
        }, ct);

        try
        {
            await notificationService.NotifyAsync(new NotificationMessage(
                user.Email,
                "Confirm your email address",
                $"Use this token to confirm your email (expires {expiresAt:u}): {tokenValue}"), ct);
        }
        catch
        {
            // Best-effort, as everywhere else — a failed send must not undo the account creation
            // that triggered it. The user can request a resend.
        }
    }

    internal static string GenerateUnsubscribeToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private async Task IssuePasswordResetAsync(AppUser user, CancellationToken ct)
    {
        var tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.Add(PasswordResetTokenLifetime);

        await passwordResetTokens.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = AuthTokenIssuer.Hash(tokenValue),
            ExpiresAt = expiresAt
        }, ct);

        await notificationService.NotifyAsync(new NotificationMessage(
            user.Email,
            "Reset your Vastora password",
            $"Use this token to reset your password (expires {expiresAt:u}): {tokenValue}"), ct);
    }

    private async Task<AuthResponse> AuthenticateAsync(AppUser user, string password, string ip, CancellationToken ct)
    {
        if (!passwordHasher.Verify(password, user.PasswordHash))
        {
            throw new UnauthorizedAppException();
        }

        if (user.Status == UserStatus.PendingVerification)
        {
            throw new ForbiddenException("Confirm your email address before signing in.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException("This account is not active.");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await users.UpdateAsync(user, ct);

        return await tokenIssuer.IssueAsync(user, ip, ct);
    }

    private async Task<Business> ResolveBusinessAsync(string businessSlug, CancellationToken ct)
    {
        var business = await businesses.FindOneAsync(b => b.Slug == businessSlug, ct)
            ?? throw new NotFoundException(nameof(Business), businessSlug);

        if (business.Status != BusinessStatus.Active)
        {
            throw new ForbiddenException("This shop is not currently accepting accounts.");
        }

        return business;
    }
}
