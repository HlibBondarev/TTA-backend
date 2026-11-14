using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// Tests for <see cref="BaseApiTest"/> verifying configuration watcher disabling
/// and deterministic resource disposal to prevent Linux inotify exhaustion.
/// </summary>
public class BaseApiTestTests(DatabaseFixture fixture, ITestOutputHelper output)
    : BaseApiTest(fixture, output)
{
    /// <summary>
    /// Verifies that the static constructor sets <c>DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE</c> to <c>"false"</c>.
    /// </summary>
    [Fact]
    public void Constructor_ShouldDisableHostBuilderReloadConfigOnChangeEnvironmentVariable()
    {
        // Act
        var envValue = Environment.GetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE");

        // Assert
        envValue.Should().Be("false");
    }

    /// <summary>
    /// Verifies that all registered <see cref="JsonConfigurationProvider"/> instances in the test host
    /// have <see cref="FileConfigurationSource.ReloadOnChange"/> set to <c>false</c>.
    /// </summary>
    [Fact]
    public void Factory_ShouldDisableReloadOnChange_ForAllJsonConfigurationSources()
    {
        // Arrange
        var configuration = Factory.Services.GetRequiredService<IConfiguration>() as IConfigurationRoot;

        // Assert
        configuration.Should().NotBeNull();
        var jsonProviders = configuration!.Providers.OfType<JsonConfigurationProvider>().ToList();
        jsonProviders.Should().NotBeEmpty();
        jsonProviders.Should().OnlyContain(provider => !provider.Source.ReloadOnChange);
    }

    /// <summary>
    /// Verifies that <see cref="BaseApiTest.DisposeAsync"/> deterministically disposes
    /// both <see cref="BaseApiTest.Client"/> and <see cref="BaseApiTest.Factory"/>.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ShouldDisposeClientAndFactory()
    {
        // Act
        await DisposeAsync();

        // Assert
        Func<Task> sendRequest = () => Client.GetAsync("/health");
        await sendRequest.Should().ThrowAsync<ObjectDisposedException>();

        Action createScope = () => Factory.Services.CreateScope();
        createScope.Should().Throw<ObjectDisposedException>();
    }
}