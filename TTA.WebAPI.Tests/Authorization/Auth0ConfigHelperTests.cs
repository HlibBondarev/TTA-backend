using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TTA.WebAPI.Authorization;

namespace TTA.WebAPI.Tests.Authorization;

public class Auth0ConfigHelperTests
{
    [Fact]
    public void GetRequiredAuth0Settings_WithValidConfig_ShouldNormalizeAuthority()
    {
        // Arrange: Authority without trailing slash
        var inMemorySettings = new Dictionary<string, string?> {
            {"Auth0:Authority", "https://dev-tta.auth0.com"},
            {"Auth0:ClientId", "test-id"},
            {"Auth0:Audience", "test-api"}
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Act
        var result = Auth0ConfigHelper.GetRequiredAuth0Settings(config);

        // Assert: Ensure slash is added and properties are mapped
        result.Authority.Should().Be("https://dev-tta.auth0.com/");
        result.AuthorizationUrl.Should().Be("https://dev-tta.auth0.com/authorize");
        result.TokenUrl.Should().Be("https://dev-tta.auth0.com/oauth/token");
    }

    [Theory]
    [InlineData("Auth0:Authority")]
    [InlineData("Auth0:ClientId")]
    [InlineData("Auth0:Audience")]
    public void GetRequiredAuth0Settings_WhenSettingIsMissing_ShouldThrowException(string missingKey)
    {
        // Arrange: Create valid dict and remove one key
        var settings = new Dictionary<string, string?> {
            {"Auth0:Authority", "https://test.com"},
            {"Auth0:ClientId", "id"},
            {"Auth0:Audience", "aud"}
        };
        settings.Remove(missingKey);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        // Act
        var act = () => Auth0ConfigHelper.GetRequiredAuth0Settings(config);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*{missingKey}*");
    }
}
