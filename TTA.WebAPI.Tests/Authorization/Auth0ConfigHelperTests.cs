using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TTA.WebAPI.Authorization;

namespace TTA.WebAPI.Tests.Authorization;

/// <summary>
/// Tests for Auth0ConfigHelper to ensure configuration validation and normalization logic.
/// </summary>
public class Auth0ConfigHelperTests
{
    [Fact]
    public void GetRequiredAuth0Settings_WithValidConfig_ShouldNormalizeAuthority()
    {
        // Arrange: Authority without trailing slash and now including Namespace
        var inMemorySettings = new Dictionary<string, string?> {
            {"Auth0:Authority", "https://dev-tta.auth0.com"},
            {"Auth0:ClientId", "test-id"},
            {"Auth0:Audience", "test-api"},
            {"Auth0:Namespace", "https://tta-api.com/"} // Added missing required key
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        var result = Auth0ConfigHelper.GetRequiredAuth0Settings(config);

        // Assert: Ensure slash is added and properties are mapped correctly
        result.Authority.Should().Be("https://dev-tta.auth0.com/");
        result.AuthorizationUrl.Should().Be("https://dev-tta.auth0.com/authorize");
        result.TokenUrl.Should().Be("https://dev-tta.auth0.com/oauth/token");
        result.Namespace.Should().Be("https://tta-api.com/");
    }

    [Theory]
    [InlineData("Auth0:Authority")]
    [InlineData("Auth0:ClientId")]
    [InlineData("Auth0:Audience")]
    [InlineData("Auth0:Namespace")] // Added test case for the new required field
    public void GetRequiredAuth0Settings_WhenSettingIsMissing_ShouldThrowException(string missingKey)
    {
        // Arrange: Create a complete valid dictionary
        var settings = new Dictionary<string, string?> {
            {"Auth0:Authority", "https://test.com"},
            {"Auth0:ClientId", "id"},
            {"Auth0:Audience", "aud"},
            {"Auth0:Namespace", "https://ns.com/"}
        };

        // Remove one key to trigger the validation error
        settings.Remove(missingKey);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        // Act
        var act = () => Auth0ConfigHelper.GetRequiredAuth0Settings(config);

        // Assert: Expect InvalidOperationException with the missing key name in the message
        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*{missingKey}*");
    }
}