using Vastora.Application.Auth;
using Vastora.Domain.Entities;

namespace Vastora.Application.Common.Interfaces;

/// <summary>
/// Shared by AuthService (login/refresh) and TenantService/UserService (sign-up flows
/// that need to hand back a ready-to-use session for the account they just created),
/// so token issuance and refresh-token persistence only live in one place.
/// </summary>
public interface IAuthTokenIssuer
{
    Task<AuthResponse> IssueAsync(AppUser user, string createdByIp, CancellationToken ct = default);
}
