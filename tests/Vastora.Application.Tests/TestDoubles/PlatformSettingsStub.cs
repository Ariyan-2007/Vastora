using Vastora.Application.Common.Interfaces;

namespace Vastora.Application.Tests.TestDoubles;

public class PlatformSettingsStub : IPlatformSettings
{
    public bool RequireEmailVerification => false;
    public TimeSpan AbandonedCartAfter => TimeSpan.FromHours(4);
    public string PublicBaseUrl => "https://example.test";
    public string ApiBaseUrl => "https://api.example.test";
    public IReadOnlyList<string> AllowedFrontendOrigins { get; set; } = [];
}
