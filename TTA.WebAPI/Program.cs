using Serilog;
using TTA.WebAPI;

// 1. Setup early logging using a basic configuration
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("TTA Application starting up...");

    // 2. Use the built-in builder. In .NET 9, this automatically handles 
    // appsettings.json and appsettings.{Environment}.json based on the project context.
    var builder = WebApplication.CreateBuilder(args);

    // Bind Serilog to the host configuration
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // 3. Register application services via extension method
    builder.AddApplicationServices();

    var app = builder.Build();

    // 4. Setup middleware pipeline via extension method
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