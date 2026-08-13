using Vastora.Domain.Entities;

namespace Vastora.Application.Common.Interfaces;

public record AccessTokenResult(string Token, DateTime ExpiresAt);

public interface IJwtTokenService
{
    AccessTokenResult GenerateAccessToken(AppUser user);

    string GenerateRefreshTokenValue();
}
