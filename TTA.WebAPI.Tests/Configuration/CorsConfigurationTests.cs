using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TTA.WebAPI.Tests.Configuration;

public class CorsConfigurationTests
{
    [Fact]
    public void AddApplicationServices_ShouldRegisterAllowFrontendCorsPolicyWithDefaultOrigins_InDevelopment()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        // Mock required Auth0 configuration to prevent startup exceptions during AddApplicationServices
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Auth0:Authority", "https://test.auth0.com/" },
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

    [Fact]
    public void AddApplicationServices_ShouldUseConfiguredCorsOrigins_WhenProvidedInConfiguration()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });

        var customOrigin = "https://custom-frontend.com";

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Auth0:Authority", "https://test.auth0.com/" },
            { "Auth0:Domain", "test.auth0.com" },
            { "Auth0:ClientId", "test-client-id" },
            { "Auth0:Audience", "https://test-api" },
            { "Auth0:Namespace", "https://test-namespace.com/" },
            { "Cors:AllowedOrigins:0", customOrigin }
        });

        // Act
        builder.AddApplicationServices();
        using var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert
        var corsOptions = serviceProvider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = corsOptions.GetPolicy("AllowFrontend");

        policy.Should().NotBeNull();
        policy!.Origins.Should().Contain(customOrigin);
        policy.Origins.Should().NotContain("https://localhost:5173");
    }

    [Fact]
    public void AddApplicationServices_ShouldHaveEmptyOriginsInNonDevelopment_WhenNoOriginsConfigured()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Auth0:Authority", "https://test.auth0.com/" },
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
        var policy = corsOptions.GetPolicy("AllowFrontend");

        policy.Should().NotBeNull();
        policy!.Origins.Should().BeEmpty();
    }

    [Fact]
    public void AddApplicationServices_ShouldUseConfiguredCorsHeadersMethodsAndCredentials_WhenProvidedInConfiguration()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Auth0:Authority", "https://test.auth0.com/" },
            { "Auth0:Domain", "test.auth0.com" },
            { "Auth0:ClientId", "test-client-id" },
            { "Auth0:Audience", "https://test-api" },
            { "Auth0:Namespace", "https://test-namespace.com/" },
            { "Cors:AllowedHeaders:0", "X-Custom-Header" },
            { "Cors:AllowedMethods:0", "POST" },
            { "Cors:AllowCredentials", "false" }
        });

        // Act
        builder.AddApplicationServices();
        using var serviceProvider = builder.Services.BuildServiceProvider();

        // Assert
        var corsOptions = serviceProvider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = corsOptions.GetPolicy("AllowFrontend");

        policy.Should().NotBeNull();
        policy!.AllowAnyHeader.Should().BeFalse();
        policy.Headers.Should().Contain("X-Custom-Header");
        policy.AllowAnyMethod.Should().BeFalse();
        policy.Methods.Should().Contain("POST");
        policy.SupportsCredentials.Should().BeFalse();
    }
}