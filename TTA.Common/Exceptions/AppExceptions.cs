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
    /// Initializes a new instance of the <see cref="BaseException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="statusCode">The HTTP status code associated with the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a null reference.</param>
    protected BaseException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Exception thrown when the request contains invalid data (HTTP 400).
/// </summary>
public class BadRequestException(string message = "Bad Request", Exception? innerException = null)
    : BaseException(message, 400, innerException);

/// <summary>
/// Exception thrown when authentication is required or has failed (HTTP 401).
/// </summary>
public class UnauthorizedException(string message = "Unauthorized access", Exception? innerException = null)
    : BaseException(message, 401, innerException);

/// <summary>
/// Exception thrown when the authenticated user does not have permission to access the resource (HTTP 403).
/// </summary>
public class ForbiddenException(string message = "Access forbidden", Exception? innerException = null)
    : BaseException(message, 403, innerException);

/// <summary>
/// Exception thrown when the requested resource was not found (HTTP 404).
/// </summary>
public class NotFoundException(string message = "The requested resource was not found", Exception? innerException = null)
    : BaseException(message, 404, innerException);

/// <summary>
/// Exception thrown when a business rule conflict occurs (HTTP 409).
/// </summary>
public class ConflictException : BaseException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ConflictException(string message = "A conflict occurred with the current state of the resource")
        : base(message, 409) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause of the current exception.</param>
    public ConflictException(string message, Exception? innerException)
        : base(message, 409, innerException) { }
}