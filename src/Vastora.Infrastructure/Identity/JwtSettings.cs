namespace Vastora.Infrastructure.Identity;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "Vastora";

    public string Audience { get; set; } = "VastoraClients";

    public int AccessTokenMinutes { get; set; } = 30;
}
