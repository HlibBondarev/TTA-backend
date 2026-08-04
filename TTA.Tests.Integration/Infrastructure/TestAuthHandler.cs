using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// A custom authentication handler that automatically signs in a test user with predefined claims.
/// Supports static flags to customize or disable authentication claims for specific test scenarios.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// Static flag to toggle the authentication result. Set to false to simulate 401 Unauthorized.
    /// </summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional custom user ID claim value for testing JIT provisioning.
    /// Set to null to restore default behavior (<see cref="BaseApiTest.TestUserId"/>).
    /// </summary>
    public static string? CustomUserId { get; set; }

    /// <summary>
    /// Optional custom email claim value for testing missing/invalid email claims.
    /// Set to null to restore default behavior, or <see cref="string.Empty"/> to omit the claim.
    /// </summary>
    public static string? CustomEmail { get; set; }

    /// <summary>
    /// Optional custom display name claim value for testing fallback logic.
    /// Set to null to restore default behavior, or <see cref="string.Empty"/> to omit the claim.
    /// </summary>
    public static string? CustomDisplayName { get; set; }

    /// <summary>
    /// Handles the authentication process by creating a successful <see cref="AuthenticateResult"/> 
    /// if <see cref="IsEnabled"/> is true; otherwise, returns a failure.
    /// </summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!IsEnabled)
        {
            return Task.FromResult(AuthenticateResult.Fail("Test authentication is disabled."));
        }

        var userId = CustomUserId ?? BaseApiTest.TestUserId;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("sub", userId)
        };

        var email = CustomEmail ?? "test@example.com";
        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim("https://tta-api.com/email", email));
        }

        var displayName = CustomDisplayName ?? "TestUser";
        if (!string.IsNullOrEmpty(displayName))
        {
            claims.Add(new Claim("https://tta-api.com/display_name", displayName));
        }

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}