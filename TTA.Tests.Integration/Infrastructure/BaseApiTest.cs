using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public const string TestUserId = "auth0|test-user";

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseApiTest"/> class.
    /// Configures the test server by mocking the database and authentication schemes.
    /// </summary>
    /// <param name="fixture">The shared database fixture instance.</param>
    protected BaseApiTest(DatabaseFixture fixture) : base(fixture)
    {
        // Defensive reset to ensure every test class starts with a known auth state
        TestAuthHandler.IsEnabled = true;

        // Build configuration before the host starts to satisfy Auth0ConfigHelper requirements
        var testConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth0:Authority"] = "https://test.auth0.com/",
                ["Auth0:ClientId"] = "test-client",
                ["Auth0:Audience"] = "test-api",
                ["Auth0:Namespace"] = "https://tta-api.com/"
            })
            .Build();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            // Inject the configuration early into the host builder
            builder.UseConfiguration(testConfig);

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