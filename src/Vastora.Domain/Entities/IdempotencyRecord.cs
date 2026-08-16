using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.17. Records the outcome of a mutating request keyed by the caller's Idempotency-Key, so a
/// retry replays the original response instead of performing the operation twice. Scoped by
/// caller as well as key — one tenant's key must never collide with another's.
/// </summary>
public class IdempotencyRecord : BaseEntity
{
    /// <summary>Caller-supplied Idempotency-Key header value.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Who sent it — user id for authenticated calls, cart token for guests.</summary>
    public string CallerId { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Hash of the request body. A repeat of the same key with a *different* payload is a client
    /// bug, not a retry, and is rejected rather than silently replaying the wrong response.
    /// </summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Serialised response body, replayed verbatim on a retry.</summary>
    public string ResponseBody { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public DateTime ExpiresAt { get; set; }
}
