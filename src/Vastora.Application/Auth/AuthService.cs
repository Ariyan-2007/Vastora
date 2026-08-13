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
    IPasswordHasher passwordHasher,
    IAuthTokenIssuer tokenIssuer) : IAuthService
{
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
