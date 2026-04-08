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
    protected BaseException(string message, int statusCode, Exception? innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Exception thrown when a request is invalid or cannot be processed (HTTP 400).
/// </summary>
public class BadRequestException : BaseException
{
    public BadRequestException(string message = "Bad Request", Exception? innerException = null)
        : base(message, 400, innerException!) { }
}

/// <summary>
/// Exception thrown when authentication is required and has failed (HTTP 401).
/// </summary>
public class UnauthorizedException : BaseException
{
    public UnauthorizedException(string message = "Unauthorized access", Exception? innerException = null)
        : base(message, 401, innerException!) { }
}

/// <summary>
/// Exception thrown when the server refuses to authorize the request (HTTP 403).
/// </summary>
public class ForbiddenException : BaseException
{
    public ForbiddenException(string message = "Access forbidden", Exception? innerException = null)
        : base(message, 403, innerException!) { }
}

/// <summary>
/// Exception thrown when the requested resource could not be found (HTTP 404).
/// </summary>
public class NotFoundException : BaseException
{
    public NotFoundException(string message = "The requested resource was not found", Exception? innerException = null)
        : base(message, 404, innerException!) { }
}

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
    public ConflictException(string message, Exception? innerException)
        : base(message, 409, innerException) { }
}
