using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.API.Filters;

/// <summary>
/// §9.17. Marks an endpoint as replay-safe: if the caller sends an <c>Idempotency-Key</c> header,
/// the first response is stored and any repeat of the same key replays it instead of performing
/// the operation again.
///
/// Opt-in per endpoint rather than global, because idempotency only makes sense where a repeat is
/// genuinely destructive — checkout being the case that motivated it. A double-tap on a flaky
/// connection used to place two real orders and deduct stock twice.
///
/// The key is optional: a client that doesn't send one gets the old behaviour, so this is
/// additive and breaks no existing caller.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<IdempotencyFilter>(serviceProvider);
}

/// <inheritdoc cref="IdempotentAttribute"/>
public class IdempotencyFilter(
    IMongoRepository<IdempotencyRecord> records,
    ICurrentUserContext currentUser,
    ILogger<IdempotencyFilter> logger) : IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";

    /// <summary>Long enough to cover a client's retry window, short enough that keys don't accumulate forever.</summary>
    private static readonly TimeSpan RecordLifetime = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            await next();
            return;
        }

        var key = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            await next();
            return;
        }

        // Scoped by caller as well as key: one tenant's key must never collide with another's.
        // Guests have no user id, so the cart token stands in as their identity.
        var callerId = string.IsNullOrEmpty(currentUser.UserId)
            ? http.Request.Headers["X-Cart-Token"].ToString() is { Length: > 0 } cartToken
                ? $"guest:{cartToken}"
                : $"ip:{http.Connection.RemoteIpAddress}"
            : currentUser.UserId;

        var requestHash = HashArguments(context.ActionArguments);
        var existing = await records.FindOneAsync(r => r.CallerId == callerId && r.Key == key, http.RequestAborted);

        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                // Same key, different payload. That is a client bug, not a retry — replaying the
                // stored response would silently answer a question that was never asked.
                context.Result = new ObjectResult(new
                {
                    title = "Idempotency key reuse",
                    detail = $"This {HeaderName} was already used with a different request body.",
                    status = StatusCodes.Status409Conflict
                })
                { StatusCode = StatusCodes.Status409Conflict };
                return;
            }

            context.Result = new ContentResult
            {
                Content = existing.ResponseBody,
                ContentType = "application/json",
                StatusCode = existing.StatusCode
            };

            http.Response.Headers["Idempotency-Replayed"] = "true";
            return;
        }

        var executed = await next();

        // Only successful outcomes are recorded. Replaying a failure would pin a transient error
        // in place for 24 hours and make the retry that would have worked impossible.
        if (executed.Exception is not null || executed.Result is not ObjectResult { Value: not null } result)
        {
            return;
        }

        var status = result.StatusCode ?? StatusCodes.Status200OK;
        if (status is < 200 or >= 300)
        {
            return;
        }

        try
        {
            await records.AddAsync(new IdempotencyRecord
            {
                Key = key,
                CallerId = callerId,
                Endpoint = $"{http.Request.Method} {http.Request.Path}",
                RequestHash = requestHash,
                ResponseBody = JsonSerializer.Serialize(result.Value, JsonOptions),
                StatusCode = status,
                ExpiresAt = DateTime.UtcNow.Add(RecordLifetime)
            }, http.RequestAborted);
        }
        catch (Exception ex)
        {
            // A duplicate-key race here means a concurrent identical request already stored the
            // record — which is the outcome we wanted anyway. Never fail the response over it.
            logger.LogWarning(ex, "Could not store idempotency record for key {Key}", key);
        }
    }

    /// <summary>
    /// Hashes only the arguments that describe *what was asked for*. Framework-injected
    /// parameters are excluded — a CancellationToken is not serialisable at all (it exposes a
    /// native handle), and an uploaded file's stream is neither serialisable nor re-readable.
    /// Including them threw on every request that carried an Idempotency-Key.
    /// </summary>
    private static string HashArguments(IDictionary<string, object?> arguments)
    {
        var meaningful = arguments
            .Where(a => a.Value is not (null or CancellationToken or IFormFile or IFormFileCollection))
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);

        var json = JsonSerializer.Serialize(meaningful, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
