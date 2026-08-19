using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Vastora.Application.Common.Interfaces;

namespace Vastora.Infrastructure.Configuration;

/// <inheritdoc cref="IPlatformSettings"/>
public class PlatformSettings(IConfiguration configuration, IHttpContextAccessor httpContextAccessor) : IPlatformSettings
{
    public bool RequireEmailVerification =>
        configuration.GetValue("Auth:RequireEmailVerification", false);

    public TimeSpan AbandonedCartAfter =>
        TimeSpan.FromHours(configuration.GetValue("Notifications:AbandonedCartAfterHours", 4));

    /// <inheritdoc cref="IPlatformSettings.PublicBaseUrl"/>
    public string PublicBaseUrl =>
        configuration["Platform:PublicBaseUrl"] ?? "http://localhost:5000";

    /// <inheritdoc cref="IPlatformSettings.AllowedFrontendOrigins"/>
    public IReadOnlyList<string> AllowedFrontendOrigins =>
        configuration.GetSection("Platform:AllowedFrontendOrigins").Get<string[]>() ?? [];

    /// <summary>
    /// Whatever request is actually in flight already knows the one thing a static config value
    /// can only guess at — the address someone is actually reaching *this API* on right now (a
    /// dev tunnel today, the real domain in production, no manual config either way).
    /// `X-Forwarded-Proto` wins over the connection's own scheme because a tunnel/proxy (ngrok, a
    /// load balancer) terminates TLS itself and forwards to Kestrel over plain HTTP —
    /// `Request.Scheme` alone would report "http" for a link the outside world can only reach
    /// over "https".
    /// </summary>
    public string ApiBaseUrl
    {
        get
        {
            var request = httpContextAccessor.HttpContext?.Request;
            if (request is not null)
            {
                var scheme = request.Headers.TryGetValue("X-Forwarded-Proto", out var forwardedProto) && forwardedProto.Count > 0
                    ? forwardedProto[0]!.Split(',')[0].Trim()
                    : request.Scheme;
                return $"{scheme}://{request.Host}";
            }

            return configuration["Platform:ApiBaseUrl"] ?? "http://localhost:5276";
        }
    }
}
