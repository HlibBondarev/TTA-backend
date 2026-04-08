namespace TTA.Common.Exceptions;

/// <summary>
/// Represents the base class for custom application exceptions with an associated HTTP status code.
/// </summary>
public abstract class BaseException : Exception
{
    /// <summary>
    /// Gets the HTTP status code associated with the exception.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="statusCode">The HTTP status code associated with the error.</param>
    protected BaseException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseException"/> class with a specified error message 
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="statusCode">The HTTP status code associated with the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    protected BaseException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
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

/// <summary>
/// Exception thrown when a business rule conflict occurs (HTTP 409).
/// </summary>
public class ConflictException : BaseException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ConflictException(string message = "A conflict occurred with the current state of the resource")
        : base(message, 409) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class with a reference to the inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public ConflictException(string message, Exception innerException)
        : base(message, 409, innerException) { }
}
