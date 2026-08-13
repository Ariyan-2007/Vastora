using System.Security.Cryptography;
using System.Text;
using Vastora.Application.Auth;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.Common;

public class AuthTokenIssuer(IJwtTokenService jwtTokenService, IMongoRepository<RefreshToken> refreshTokens)
    : IAuthTokenIssuer
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<AuthResponse> IssueAsync(AppUser user, string createdByIp, CancellationToken ct = default)
    {
        var accessToken = jwtTokenService.GenerateAccessToken(user);
        var refreshTokenValue = jwtTokenService.GenerateRefreshTokenValue();
        var expiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime);

        await refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshTokenValue),
            ExpiresAt = expiresAt,
            CreatedByIp = createdByIp
        }, ct);

        return new AuthResponse(
            accessToken.Token,
            accessToken.ExpiresAt,
            refreshTokenValue,
            expiresAt,
            new UserSummaryResponse(user.Id, user.FullName, user.Email, user.Role, user.TenantId, user.BusinessId, user.Status));
    }

    internal static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
