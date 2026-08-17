namespace Vastora.Application.Common.Interfaces;

/// <summary>
/// Deployment-level switches the Application layer needs to read. An interface rather than an
/// injected IConfiguration so Application keeps depending only on abstractions — the same reason
/// IFileStorageService and INotificationService exist.
/// </summary>
public interface IPlatformSettings
{
    /// <summary>
    /// §9.34. When true, a customer must confirm their email before they can sign in.
    ///
    /// Defaults to **false**, and that is a deliberate compromise rather than an oversight: the
    /// only INotificationService implementation logs instead of sending (§9.10), so defaulting
    /// this on would lock every new customer out of every shop until an operator read the server
    /// log. Turn it on in the same change that wires a real email provider — the switch exists so
    /// that is a config edit, not a code change.
    /// </summary>
    bool RequireEmailVerification { get; }

    /// <summary>§9.36. How long a cart sits untouched before it counts as abandoned.</summary>
    TimeSpan AbandonedCartAfter { get; }

    /// <summary>Base URL used to build customer-facing links (verification, unsubscribe, tracking).</summary>
    string PublicBaseUrl { get; }
}
