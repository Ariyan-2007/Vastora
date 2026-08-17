using Microsoft.Extensions.Configuration;
using Vastora.Application.Common.Interfaces;

namespace Vastora.Infrastructure.Configuration;

/// <inheritdoc cref="IPlatformSettings"/>
public class PlatformSettings(IConfiguration configuration) : IPlatformSettings
{
    public bool RequireEmailVerification =>
        configuration.GetValue("Auth:RequireEmailVerification", false);

    public TimeSpan AbandonedCartAfter =>
        TimeSpan.FromHours(configuration.GetValue("Notifications:AbandonedCartAfterHours", 4));

    public string PublicBaseUrl =>
        configuration["Platform:PublicBaseUrl"] ?? "http://localhost:5000";
}
