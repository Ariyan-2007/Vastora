namespace Vastora.Application.Common.Exceptions;

/// <summary>Base for exceptions that should be mapped to a specific HTTP status by the API's exception middleware.</summary>
public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string entity, string key)
    : AppException($"{entity} '{key}' was not found.");

public sealed class ConflictException(string message) : AppException(message);

public sealed class ForbiddenException(string message = "You do not have access to this resource.") : AppException(message);

public sealed class UnauthorizedAppException(string message = "Invalid credentials.") : AppException(message);

public sealed class ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
