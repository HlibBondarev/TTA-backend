using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TTA.DataAccess.Repository.Base;
using Xunit.Abstractions;

namespace TTA.Tests.Integration.Infrastructure;

/// <summary>
/// Base class for API integration tests. 
/// Provides a configured <see cref="HttpClient"/> and <see cref="WebApplicationFactory{TEntryPoint}"/> 
/// with mocked authentication and database connection.
/// </summary>
public abstract class BaseApiTest : BaseIntegrationTest
{
    protected readonly HttpClient Client;
    protected readonly WebApplicationFactory<Program> Factory;
    public const string TestUserId = "auth0|test-user";

    static BaseApiTest()
    {
        Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");
    }

    protected BaseApiTest(DatabaseFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        TestAuthHandler.IsEnabled = true;

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
            builder.UseConfiguration(testConfig);

            builder.ConfigureAppConfiguration((_, config) =>
            {
                foreach (var source in config.Sources.OfType<JsonConfigurationSource>())
                {
                    source.ReloadOnChange = false;
                }
            });

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                // We add a simple action to direct logs to xUnit output
                logging.AddProvider(new XUnitLoggerProvider(output));
            });

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

        try
        {
            Client = Factory.CreateClient();
        }
        catch
        {
            Factory.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Disposes the HTTP client and test web application factory before resetting the database state.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public override async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await base.DisposeAsync();
    }
}

// Simple internal provider to avoid NuGet dependency issues
internal class XUnitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new XUnitLogger(output, categoryName);
    public void Dispose() { }
}

internal class XUnitLogger(ITestOutputHelper output, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try { output.WriteLine($"{logLevel}: {categoryName}[{eventId}] {formatter(state, exception)} {(exception != null ? "\n" + exception : "")}"); }
        catch { /* Output helper might be disposed */ }
    }
}