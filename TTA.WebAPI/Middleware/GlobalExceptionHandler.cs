using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Security.Authentication;
using System.Text;
using TTA.Common.Exceptions;
using TTA.Common.Extensions;

namespace TTA.WebAPI.Middleware;

/// <summary>
/// Provides a centralized exception handling mechanism to intercept unhandled exceptions 
/// and return standardized ProblemDetails responses.
/// </summary>
/// <param name="logger">The logger used for capturing error details.</param>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Attempts to handle the exception that occurred during the request execution.
    /// This method formats the response as a standardized ProblemDetails JSON object.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The exception to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation.</param>
    /// <returns>True if the exception was handled; otherwise, false.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Extract the root cause if the exception is wrapped (e.g., in an AggregateException)
        var actualException = exception is AggregateException ae ? ae.InnerException ?? exception : exception;

        logger.LogError(actualException, "An unhandled exception has occurred: {Message}", actualException.Message);

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        // Use the unwrapped exception for status code and message mapping
        var (statusCode, baseMessage) = MapException(actualException);

        // Detailed collection of validation errors for both Detail string and Extensions dictionary
        var validationErrors = new Dictionary<string, string[]>();
        var detailBuilder = new StringBuilder(baseMessage);

        // Extract structured data if the actual exception contains entries in the Data dictionary
        if (actualException.Data.Count > 0)
        {
            // Efficiency fix: Use char ' ' instead of string " "
            detailBuilder.Append(' ');
            foreach (DictionaryEntry entry in actualException.Data)
            {
                var key = entry.Key.ToString() ?? "Error";
                var values = entry.Value as string[] ?? [entry.Value?.ToString() ?? "Unknown error"];

                validationErrors.Add(key, values);

                // Formatting the string to satisfy "Field: Error1, Error2" pattern in tests
                detailBuilder.Append($"{key}: {string.Join(", ", values)}. ");
            }
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode.GetTitleForStatus(),
            Detail = detailBuilder.ToString().Trim(),
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        // If structured errors exist, add them to the extensions for programmatic access
        if (validationErrors.Count > 0)
        {
            problemDetails.Extensions["errors"] = validationErrors;
        }

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }

    /// <summary>
    /// Maps various exception types to appropriate HTTP status codes and initial error messages.
    /// </summary>
    /// <param name="exception">The exception to map.</param>
    /// <returns>A tuple containing the status code and error message.</returns>
    private static (int StatusCode, string Message) MapException(Exception exception) => exception switch
    {
        BaseException customEx => (customEx.StatusCode, customEx.Message),

        AuthenticationException ex => (StatusCodes.Status401Unauthorized, ex.Message),

        UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Access denied."),

        ValidationException ex => (StatusCodes.Status400BadRequest, ex.Message),

        KeyNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),

        NpgsqlException => (StatusCodes.Status500InternalServerError, "Database error occurred."),

        _ => (StatusCodes.Status500InternalServerError, "An unexpected internal server error occurred.")
    };
}