using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TTA.WebAPI.Tests.Configuration;

public class CorsConfigurationTests
{
    [Fact]
    public void AddApplicationServices_ShouldRegisterAllowFrontendCorsPolicy()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        // Mock required Auth0 configuration to prevent startup exceptions during AddApplicationServices
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Auth0:Domain", "test.auth0.com" },
            { "Auth0:ClientId", "test-client-id" },
            { "Auth0:Audience", "https://test-api" },
            { "Auth0:Namespace", "https://test-namespace.com/" }
        });

        // Act
        builder.AddApplicationServices();
        using var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert
        var corsOptions = serviceProvider.GetRequiredService<IOptions<CorsOptions>>().Value;
        corsOptions.Should().NotBeNull();

        var policy = corsOptions.GetPolicy("AllowFrontend");
        policy.Should().NotBeNull();
        policy!.AllowAnyHeader.Should().BeTrue();
        policy.AllowAnyMethod.Should().BeTrue();
        policy.SupportsCredentials.Should().BeTrue();
        policy.Origins.Should().Contain("https://localhost:5173");
    }
}