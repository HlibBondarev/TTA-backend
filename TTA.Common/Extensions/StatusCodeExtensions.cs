using Microsoft.AspNetCore.Http;

namespace TTA.Common.Extensions;

/// <summary>
/// Provides extension methods for HTTP status codes.
/// </summary>
public static class StatusCodeExtensions
{
    /// <summary>
    /// Returns a human-readable title corresponding to the provided HTTP status code.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>A string representing the title of the status code.</returns>
    public static string GetTitleForStatus(this int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        _ => $"Status {statusCode}" // neutral representation
    };
}
