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

public abstract class BaseApiTest : BaseIntegrationTest
{
    protected readonly HttpClient Client;
    protected const string TestUserId = "auth0|test-user-id";

    protected BaseApiTest(DatabaseFixture fixture) : base(fixture)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbConnectionFactory>();
                services.AddSingleton(Fixture.ConnectionFactory);

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TestScheme";
                    options.DefaultChallengeScheme = "TestScheme";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });
            });
        });

        Client = factory.CreateClient();
    }
}

// Move this class OUTSIDE of BaseApiTest to resolve CS0115 and CS0246
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Explicit type to avoid collection expression issues
        Claim[] claims = [
            new Claim(ClaimTypes.NameIdentifier, "auth0|test-user-id"),
            new Claim("sub", "auth0|test-user-id")
        ];

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}