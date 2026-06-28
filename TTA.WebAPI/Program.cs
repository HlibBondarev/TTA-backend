using Serilog;
using TTA.WebAPI;

// 1. Setup early logging using a basic configuration
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("TTA Application starting up...");

    // 2. Load variables from .env file
    DotNetEnv.Env.Load();

    // 3. Get ports from environment variables or use defaults
    var httpsPort = Environment.GetEnvironmentVariable("API_PORT_HTTPS") ?? "5001";
    var httpPort = Environment.GetEnvironmentVariable("API_PORT_HTTP") ?? "5002";

    // 4. Use the built-in builder. In .NET 9, this automatically handles 
    // appsettings.json and appsettings.{Environment}.json based on the project context.
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls($"https://localhost:{httpsPort};http://localhost:{httpPort}");

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