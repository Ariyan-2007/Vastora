namespace Vastora.Infrastructure.Notifications;

public class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Every outbound email is sent from this address, regardless of caller — there is no per-Tenant/Business sender identity.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Vastora";
}
