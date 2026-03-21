namespace TTA.Common.Exceptions;

/// <summary>
/// Represents the base class for custom application exceptions with an associated HTTP status code.
/// </summary>
/// <param name="message">The error message that explains the reason for the exception.</param>
/// <param name="statusCode">The HTTP status code associated with the error.</param>
public abstract class BaseException(string message, int statusCode) : Exception(message)
{
    /// <summary>
    /// Gets the HTTP status code associated with the exception.
    /// </summary>
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Exception thrown when a request is invalid or cannot be processed (HTTP 400).
/// </summary>
public class BadRequestException(string message = "Bad Request")
    : BaseException(message, 400);

/// <summary>
/// Exception thrown when authentication is required and has failed or has not yet been provided (HTTP 401).
/// </summary>
public class UnauthorizedException(string message = "Unauthorized access")
    : BaseException(message, 401);

/// <summary>
/// Exception thrown when the server understands the request but refuses to authorize it (HTTP 403).
/// </summary>
public class ForbiddenException(string message = "Access forbidden")
    : BaseException(message, 403);

/// <summary>
/// Exception thrown when the requested resource could not be found (HTTP 404).
/// </summary>
public class NotFoundException(string message = "The requested resource was not found")
    : BaseException(message, 404);
