using System.Security.Cryptography;
using Vastora.Application.Businesses;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Notifications;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Auth;

public class AuthService(
    IMongoRepository<AppUser> users,
    IMongoRepository<Business> businesses,
    IMongoRepository<TenantAccount> tenants,
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
        var shopOrigins = string.IsNullOrWhiteSpace(business.ShopDomain) ? [] : new[] { business.ShopDomain };
        await IssueEmailVerificationAsync(user, business, request.RedirectBaseUrl, shopOrigins, ct);

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

    public async Task RequestPasswordResetAsync(string email, string? redirectBaseUrl = null, CancellationToken ct = default)
    {
        var user = await users.FindOneAsync(u => u.Email == email && u.Role != UserRole.Customer, ct);
        if (user is not null)
        {
            var (business, allowedOrigins) = await ResolveStaffRealmAsync(user, ct);
            await IssuePasswordResetAsync(user, business, redirectBaseUrl, allowedOrigins, ct);
        }
    }

    public async Task RequestStorefrontPasswordResetAsync(string businessSlug, string email, string? redirectBaseUrl = null, CancellationToken ct = default)
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
            var allowedOrigins = string.IsNullOrWhiteSpace(business.ShopDomain) ? [] : new[] { business.ShopDomain };
            await IssuePasswordResetAsync(user, business, redirectBaseUrl, allowedOrigins, ct);
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

    public async Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        if (!passwordHasher.Verify(currentPassword, user.PasswordHash))
        {
            throw new UnauthorizedAppException("Current password is incorrect.");
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await users.UpdateAsync(user, ct);

        // Same "something may be compromised" signal as a token-based reset — sign out
        // everywhere, including the session that made this change, forcing a fresh login.
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

        var business = string.IsNullOrEmpty(user.BusinessId) ? null : await businesses.GetByIdAsync(user.BusinessId, ct);
        // No request body on this endpoint (resend is a bare authenticated POST) — no
        // client-supplied origin to validate, so this always falls back to PublicBaseUrl regardless
        // of allowed origins.
        await IssueEmailVerificationAsync(user, business, null, [], ct);
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

    private async Task IssueEmailVerificationAsync(AppUser user, Business? business, string? redirectBaseUrl, IReadOnlyList<string> allowedOrigins, CancellationToken ct)
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
            business = BusinessAssetUrls.ResolveLogo(business, platformSettings.ApiBaseUrl);
            var link = $"{ResolveLinkBase(redirectBaseUrl, allowedOrigins).TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(tokenValue)}";
            var (subject, plainBody, htmlBody) = EmailTemplates.VerifyEmail(business, user.FullName, link, expiresAt);
            await notificationService.NotifyAsync(new NotificationMessage(user.Email, subject, plainBody, htmlBody, BusinessId: business?.Id), ct);
        }
        catch
        {
            // Best-effort, as everywhere else — a failed send must not undo the account creation
            // that triggered it. The user can request a resend.
        }
    }

    internal static string GenerateUnsubscribeToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private async Task IssuePasswordResetAsync(AppUser user, Business? business, string? redirectBaseUrl, IReadOnlyList<string> allowedOrigins, CancellationToken ct)
    {
        var tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.Add(PasswordResetTokenLifetime);

        await passwordResetTokens.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = AuthTokenIssuer.Hash(tokenValue),
            ExpiresAt = expiresAt
        }, ct);

        business = BusinessAssetUrls.ResolveLogo(business, platformSettings.ApiBaseUrl);
        var link = $"{ResolveLinkBase(redirectBaseUrl, allowedOrigins).TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(tokenValue)}";
        var (subject, plainBody, htmlBody) = EmailTemplates.PasswordReset(business, user.FullName, link, expiresAt);
        await notificationService.NotifyAsync(new NotificationMessage(user.Email, subject, plainBody, htmlBody, BusinessId: business?.Id), ct);
    }

    /// <summary>
    /// Maps a non-Customer AppUser to the Business/Tenant it's scoped to and the domain(s) that
    /// govern which redirectBaseUrl origin is trusted for their emailed links — see
    /// ResolveLinkBase. Every realm's trust anchor is now a dynamic, API-settable field rather than
    /// static config, so onboarding a new BackOffice/SuperOffice origin never requires an env edit
    /// or redeploy — except PlatformSuperAdmin, which has no Business or Tenant above it and no one
    /// but Platform config itself positioned to set one, so it alone still reads
    /// Platform:AllowedFrontendOrigins.
    /// </summary>
    private async Task<(Business? Business, IReadOnlyList<string> AllowedOrigins)> ResolveStaffRealmAsync(AppUser user, CancellationToken ct)
    {
        switch (user.Role)
        {
            case UserRole.PlatformSuperAdmin:
                return (null, platformSettings.AllowedFrontendOrigins);

            case UserRole.TenantOwner:
                var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
                var tenantOrigins = string.IsNullOrWhiteSpace(tenant?.SuperOfficeDomain) ? [] : new[] { tenant!.SuperOfficeDomain! };
                return (null, tenantOrigins);

            default: // BusinessAdmin, BusinessStaff, DeliveryAgent
                var business = string.IsNullOrEmpty(user.BusinessId) ? null : await businesses.GetByIdAsync(user.BusinessId, ct);
                var businessOrigins = string.IsNullOrWhiteSpace(business?.BackOfficeDomain) ? [] : new[] { business!.BackOfficeDomain! };
                return (business, businessOrigins);
        }
    }

    /// <summary>
    /// Picks the base URL a customer/staff-facing link (verify-email, reset-password) actually
    /// points at, in priority order:
    /// <list type="number">
    /// <item>
    /// <paramref name="redirectBaseUrl"/> — whatever the caller claims is its own origin (e.g. a
    /// frontend sending <c>window.location.origin</c>) — <strong>but only if it exactly matches
    /// one of <paramref name="allowedOrigins"/></strong>. Never trusted outright: honoring an
    /// unvalidated client-supplied redirect target on a password-reset email is a real
    /// account-takeover vector (an attacker requests a reset for the victim's email with their
    /// own domain as the target; the victim's real reset token gets emailed pointing at the
    /// attacker's phishing page, which relays it back to this API to actually change the
    /// password) — same class of bug as an open redirect, just delivered by email instead of a
    /// 302.
    /// </item>
    /// <item>
    /// The realm's own configured domain — the first of <paramref name="allowedOrigins"/> — used
    /// as-is when no <paramref name="redirectBaseUrl"/> was sent or it didn't match. This is the
    /// actual default the caller should see day to day: SuperOffice/Platform set these domains
    /// (<c>Business.ShopDomain</c>/<c>BackOfficeDomain</c>, <c>TenantAccount.SuperOfficeDomain</c>)
    /// specifically so links resolve correctly without every single request having to also supply
    /// its own origin — no poisoning risk here, since these values come from an authenticated
    /// SuperOffice/Platform write, never from the caller of this particular request.
    /// </item>
    /// <item>
    /// The platform's static <c>PublicBaseUrl</c> — true last resort, only reached when neither
    /// of the above is available (e.g. nothing has configured this realm's domain yet).
    /// </item>
    /// </list>
    /// </summary>
    private string ResolveLinkBase(string? redirectBaseUrl, IReadOnlyList<string> allowedOrigins)
    {
        var configuredDefault = allowedOrigins.Count > 0 ? NormalizeOrigin(allowedOrigins[0]) : platformSettings.PublicBaseUrl;

        if (string.IsNullOrWhiteSpace(redirectBaseUrl) || !Uri.TryCreate(redirectBaseUrl, UriKind.Absolute, out var candidate))
        {
            return configuredDefault;
        }

        foreach (var allowed in allowedOrigins)
        {
            if (Uri.TryCreate(NormalizeOrigin(allowed), UriKind.Absolute, out var allowedUri)
                && string.Equals(candidate.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Host, allowedUri.Host, StringComparison.OrdinalIgnoreCase)
                && candidate.Port == allowedUri.Port)
            {
                return $"{candidate.Scheme}://{candidate.Authority}";
            }
        }

        return configuredDefault;
    }

    /// <summary>
    /// A domain as SuperOffice/Platform typed it into a form — "antivaly.com",
    /// "http://localhost:5274" — normalized to a real origin string. Defaults to
    /// <c>https://</c> when no scheme was given, so a local dev domain (an http-only Vite/CRA
    /// dev server) must be entered with its scheme explicit to avoid an unwanted https upgrade.
    /// </summary>
    private static string NormalizeOrigin(string domain)
    {
        var withScheme = domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? domain
            : "https://" + domain;

        return Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Authority}" : withScheme;
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
