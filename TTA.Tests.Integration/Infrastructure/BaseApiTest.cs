using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using TTA.DataAccess.Repository.Base;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// Base class for API integration tests. 
/// Provides a configured <see cref="HttpClient"/> and <see cref="WebApplicationFactory{TEntryPoint}"/> 
/// with mocked authentication and database connection.
/// </summary>
public abstract class BaseApiTest : BaseIntegrationTest
{
    /// <summary>
    /// Default HTTP client with pre-configured authentication headers.
    /// </summary>
    protected readonly HttpClient Client;

    /// <summary>
    /// The underlying WebApplicationFactory used to create the test server and clients.
    /// </summary>
    protected readonly WebApplicationFactory<Program> Factory;

    /// <summary>
    /// Shared test user identifier used across integration tests.
    /// </summary>
    protected const string TestUserId = "auth0|test-user-id";

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseApiTest"/> class.
    /// Configures the test server by mocking the database and authentication schemes.
    /// </summary>
    /// <param name="fixture">The shared database fixture instance.</param>
    protected BaseApiTest(DatabaseFixture fixture) : base(fixture)
    {
        // Defensive reset to ensure every test class starts with a known auth state
        TestAuthHandler.IsEnabled = true;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                // Replace the real database connection factory with the test one
                services.RemoveAll<IDbConnectionFactory>();
                services.AddSingleton(Fixture.ConnectionFactory);

                // Configure a custom authentication scheme for testing purposes
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TestScheme";
                    options.DefaultChallengeScheme = "TestScheme";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });
            });
        });

        Client = Factory.CreateClient();
    }
}

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
            new Claim(ClaimTypes.NameIdentifier, "auth0|test-user-id"), // Accessing const via literal or BaseApiTest.TestUserId
            new Claim("sub", "auth0|test-user-id")
        ];

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}