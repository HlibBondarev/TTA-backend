using Serilog;
using TTA.WebAPI;

// 1. Setup early logging using a basic configuration
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("TTA Application starting up...");

    // 2. Load variables from .env file safely without overwriting system or orchestrator variables
    DotNetEnv.Env.NoClobber().Load();

    var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

    // 3. Use the built-in builder. In .NET 9, this automatically handles 
    // appsettings.json and appsettings.{Environment}.json based on the project context.
    var builder = WebApplication.CreateBuilder(args);

    // Apply local development ports only if ASPNETCORE_URLS is not explicitly set by the environment or Docker container
    if (string.IsNullOrWhiteSpace(configuredUrls))
    {
        var httpsPort = Environment.GetEnvironmentVariable("API_PORT_HTTPS") ?? "5001";
        var httpPort = Environment.GetEnvironmentVariable("API_PORT_HTTP") ?? "5002";
        builder.WebHost.UseUrls($"https://localhost:{httpsPort};http://localhost:{httpPort}");
    }

    // 4. Bind Serilog to the host configuration
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // 5. Bind Serilog to the host configuration
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // 6. Register application services via extension method
    builder.AddApplicationServices();

    var app = builder.Build();

    // 7. Setup middleware pipeline via extension method
    app.Configure();

    Log.Information("TTA Application has started successfully");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TTA Application terminated unexpectedly during startup");
    // Re-throw the exception so WebApplicationFactory can report the underlying issue
    throw;
}
finally
{
    Log.Information("TTA Application shut down complete");
    await Log.CloseAndFlushAsync();
}

// Required for WebApplicationFactory to access the entry point
public partial class Program { }