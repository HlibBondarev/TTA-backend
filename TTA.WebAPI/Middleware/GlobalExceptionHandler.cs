using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using System.ComponentModel.DataAnnotations;
using System.Security.Authentication;
using TTA.Common.Exceptions;
using TTA.Common.Extensions;

namespace TTA.WebAPI.Middleware;

/// <summary>
/// Provides a centralized exception handling mechanism for the entire application.
/// Intercepts all unhandled exceptions and converts them into standardized <see cref="ProblemDetails"/> responses.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Attempts to handle the specified exception and write a standardized JSON response to the client.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="exception">The exception that occurred during request processing.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the completion of the operation. 
    /// Returns <c>true</c> if the exception was successfully handled.
    /// </returns>

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "An unhandled exception has occurred: {Message}", exception.Message);

        // Guard: If headers are already sent, we cannot modify the response
        if (httpContext.Response.HasStarted)
        {
            logger.LogWarning("The response has already started, skipping GlobalExceptionHandler.");
            return false;
        }

        var (statusCode, message) = MapException(exception);
        var sanitizedErrors = GetSanitizedValidationErrors(exception);

        string? detailedMessage = sanitizedErrors != null
            ? string.Join(" | ", sanitizedErrors.Select(e => $"{e.Key}: {string.Join(", ", e.Value)}"))
            : null;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode.GetTitleForStatus(),
            Detail = detailedMessage ?? message,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (sanitizedErrors != null)
        {
            problemDetails.Extensions["errors"] = sanitizedErrors;
        }

        // Set standard RFC 7807 headers and status code
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json"; // Fix for CodeRabbit/RFC 7807

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    // New helper method to ensure data safety
    private static Dictionary<string, string[]>? GetSanitizedValidationErrors(Exception exception)
    {
        if (exception is not ValidationException || exception.Data.Count == 0)
            return null;

        var sanitized = new Dictionary<string, string[]>();

        foreach (System.Collections.DictionaryEntry entry in exception.Data)
        {
            if (entry.Key is string key && entry.Value is string[] values)
            {
                sanitized[key] = values;
            }
        }

        return sanitized.Count > 0 ? sanitized : null;
    }

    private static (int StatusCode, string Message) MapException(Exception exception) => exception switch
    {
        BaseException customEx => (customEx.StatusCode, customEx.Message),
        ArgumentNullException => (StatusCodes.Status400BadRequest, "Request data is missing."),
        InvalidOperationException => (StatusCodes.Status400BadRequest, exception.Message ?? "Invalid operation."),
        ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message ?? "Validation error."),
        AuthenticationException ex => (StatusCodes.Status401Unauthorized, ex.Message ?? "Auth failed."),
        KeyNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message ?? "Not found."),
        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access denied."),
        ValidationException ex => (StatusCodes.Status400BadRequest, ex.Message),
        NpgsqlException => (StatusCodes.Status500InternalServerError, "Database error."),
        OptionsValidationException => (StatusCodes.Status500InternalServerError, "Configuration error."),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error.")
    };
}