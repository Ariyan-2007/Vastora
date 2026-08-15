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
    IPasswordHasher passwordHasher,
    IAuthTokenIssuer tokenIssuer,
    INotificationService notificationService) : IAuthService
{
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromHours(1);

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
            Status = UserStatus.Active
        };

        await users.AddAsync(user, ct);

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
