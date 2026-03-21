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
        // Log the full exception details using the configured Serilog provider
        logger.LogError(exception, "An unhandled exception has occurred: {Message}", exception.Message);

        // Map various exception types to appropriate HTTP status codes and user-friendly messages
        var (statusCode, message) = exception switch
        {
            // Application-specific exceptions with predefined status codes (400, 403, 404)
            BaseException customEx => (customEx.StatusCode, customEx.Message),

            // Handling missing request data
            ArgumentNullException => (StatusCodes.Status400BadRequest,
                "The request data is missing. Please check your input and try again."),

            // Handling invalid logic operations
            InvalidOperationException => (StatusCodes.Status400BadRequest,
                string.IsNullOrWhiteSpace(exception.Message)
                ? "An invalid operation was attempted. Please verify your request data."
                : exception.Message),

            // Handling general argument validation errors
            ArgumentException ex => (StatusCodes.Status400BadRequest,
                string.IsNullOrWhiteSpace(ex.Message)
                ? "The provided arguments are invalid. Please check your input data."
                : ex.Message),

            // Authentication failure (401)
            AuthenticationException ex => (StatusCodes.Status401Unauthorized,
                string.IsNullOrWhiteSpace(ex.Message)
                ? "User claims could not be retrieved from the current security context."
                : ex.Message),

            // Resource not found (404)
            KeyNotFoundException ex => (StatusCodes.Status404NotFound,
                string.IsNullOrWhiteSpace(ex.Message) ? "The requested entity was not found." : ex.Message),

            // Permissions and access control (403)
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden,
                "Access denied. You do not have the required permissions to perform this action."),

            // Data annotation validation failures (400)
            ValidationException ex => (StatusCodes.Status400BadRequest, ex.Message),

            // Database layer exceptions for PostgreSQL (500)
            NpgsqlException => (StatusCodes.Status500InternalServerError,
                "A database error occurred. Please contact the administrator or try again later."),

            // Configuration/Options validation failures (500)
            OptionsValidationException => (StatusCodes.Status500InternalServerError,
                "Internal server configuration error. Please contact technical support."),

            // Fallback for any other unexpected exceptions (500)
            _ => (StatusCodes.Status500InternalServerError,
                "An unexpected internal server error occurred. Please try again later.")
        };

        // Initialize detailed message for validation errors
        string? detailedMessage = null;

        // Extract and format structured validation data if available in the exception
        if (exception is ValidationException && exception.Data.Count > 0)
        {
            var errorList = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in exception.Data)
            {
                if (entry.Value is string[] messages)
                {
                    // Format as "FieldName: error1, error2"
                    errorList.Add($"{entry.Key}: {string.Join(", ", messages)}");
                }
            }

            if (errorList.Count != 0)
            {
                detailedMessage = string.Join(" | ", errorList);
            }
        }

        // Build the ProblemDetails response based on RFC 7807
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode.GetTitleForStatus(),
            Detail = detailedMessage ?? message,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        // Add Trace ID to facilitate log correlation and troubleshooting
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        // Preserve raw validation errors in the extensions for frontend processing
        if (exception is ValidationException && exception.Data.Count > 0)
        {
            problemDetails.Extensions["errors"] = exception.Data;
        }

        // Write the response as JSON with the correct status code
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Return true to indicate that the exception has been fully processed
        return true;
    }
}