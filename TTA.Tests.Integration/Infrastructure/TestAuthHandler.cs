using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// A custom authentication handler that automatically signs in a test user with predefined claims.
/// Supports a static flag to disable authentication for specific test scenarios.
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
    /// Handles the authentication process by creating a successful <see cref="AuthenticateResult"/> 
    /// if <see cref="IsEnabled"/> is true; otherwise, returns a failure.
    /// </summary>
    /// <returns>A task that represents the asynchronous authentication operation.</returns>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!IsEnabled)
        {
            return Task.FromResult(AuthenticateResult.Fail("Test authentication is disabled."));
        }

        // Use the constant to avoid drift between handler and tests
        Claim[] claims = [
            new Claim(ClaimTypes.NameIdentifier, BaseApiTest.TestUserId),
            new Claim("sub", BaseApiTest.TestUserId),
            new Claim("https://tta-api.com/email", "test@example.com"),
            new Claim("https://tta-api.com/display_name", "TestUser")
        ];

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
