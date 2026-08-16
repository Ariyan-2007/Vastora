using System.Security.Cryptography;
using System.Text;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.ApiKeys;

public record CreateApiKeyRequest(string Name, string? BusinessId, List<string>? Scopes, DateTime? ExpiresAt);

/// <summary>
/// <paramref name="Secret"/> is the full credential (<c>keyId.secret</c>) and is returned exactly
/// once, from the create call. Only its hash is stored; there is no way to recover it.
/// </summary>
public record ApiKeyResponse(
    string Id,
    string Name,
    string KeyId,
    string? Secret,
    string BusinessId,
    List<string> Scopes,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt,
    DateTime CreatedAt);

/// <summary>Resolved identity behind a valid API key, used by the authentication handler.</summary>
public record ApiKeyPrincipal(string ApiKeyId, string TenantId, string BusinessId, IReadOnlyList<string> Scopes);

/// <summary>§9.39. Server-to-server credentials — before this the only credential was a 30-minute user JWT.</summary>
public interface IApiKeyService
{
    Task<PagedResult<ApiKeyResponse>> GetAllAsync(string tenantId, PageRequest page, CancellationToken ct = default);

    Task<ApiKeyResponse> CreateAsync(string tenantId, CreateApiKeyRequest request, CancellationToken ct = default);

    Task RevokeAsync(string tenantId, string apiKeyId, CancellationToken ct = default);

    /// <summary>Null when the presented key is unknown, revoked or expired. Touches LastUsedAt on success.</summary>
    Task<ApiKeyPrincipal?> AuthenticateAsync(string presentedKey, CancellationToken ct = default);
}

/// <inheritdoc cref="IApiKeyService"/>
public class ApiKeyService(IMongoRepository<ApiKey> apiKeys) : IApiKeyService
{
    private static readonly string[] ValidScopes = ["read", "write"];

    public async Task<PagedResult<ApiKeyResponse>> GetAllAsync(string tenantId, PageRequest page, CancellationToken ct = default)
    {
        var result = await apiKeys.FindPagedAsync(k => k.TenantId == tenantId, page, k => k.CreatedAt, ct: ct);
        return result.Map(k => Map(k, null));
    }

    public async Task<ApiKeyResponse> CreateAsync(string tenantId, CreateApiKeyRequest request, CancellationToken ct = default)
    {
        var scopes = request.Scopes is { Count: > 0 } ? request.Scopes : ["read"];

        var unknown = scopes.Except(ValidScopes).ToList();
        if (unknown.Count > 0)
        {
            throw new ConflictException($"Unknown scope(s): {string.Join(", ", unknown)}. Valid scopes are: {string.Join(", ", ValidScopes)}.");
        }

        // Split credential: a public key id that identifies the row (so lookup is an indexed
        // equality match, not a scan over every hash) and a secret half that is never stored.
        var keyId = $"vk_{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        var key = new ApiKey
        {
            TenantId = tenantId,
            BusinessId = request.BusinessId ?? string.Empty,
            Name = request.Name,
            KeyId = keyId,
            SecretHash = Hash(secret),
            Scopes = scopes,
            ExpiresAt = request.ExpiresAt
        };

        await apiKeys.AddAsync(key, ct);
        return Map(key, $"{keyId}.{secret}");
    }

    public async Task RevokeAsync(string tenantId, string apiKeyId, CancellationToken ct = default)
    {
        var key = await apiKeys.GetByIdAsync(apiKeyId, ct);
        if (key is null || key.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(ApiKey), apiKeyId);
        }

        key.RevokedAt = DateTime.UtcNow;
        await apiKeys.UpdateAsync(key, ct);
    }

    public async Task<ApiKeyPrincipal?> AuthenticateAsync(string presentedKey, CancellationToken ct = default)
    {
        var separator = presentedKey.IndexOf('.');
        if (separator <= 0 || separator == presentedKey.Length - 1)
        {
            return null;
        }

        var keyId = presentedKey[..separator];
        var secret = presentedKey[(separator + 1)..];

        var key = await apiKeys.FindOneAsync(k => k.KeyId == keyId, ct);
        if (key is null || !key.IsUsable(DateTime.UtcNow))
        {
            return null;
        }

        // Fixed-time comparison — a length-or-prefix-sensitive string compare on a credential is
        // a timing oracle, and the whole point of storing only the hash is defeated if the
        // comparison leaks how much of it matched.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Hash(secret)),
                Encoding.UTF8.GetBytes(key.SecretHash)))
        {
            return null;
        }

        key.LastUsedAt = DateTime.UtcNow;
        await apiKeys.UpdateAsync(key, ct);

        return new ApiKeyPrincipal(key.Id, key.TenantId, key.BusinessId, key.Scopes);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ApiKeyResponse Map(ApiKey k, string? secret) => new(
        k.Id, k.Name, k.KeyId, secret, k.BusinessId, k.Scopes, k.ExpiresAt, k.LastUsedAt, k.RevokedAt, k.CreatedAt);
}
