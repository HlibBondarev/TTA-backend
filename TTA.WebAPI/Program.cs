// 1. Setup early logging (Bootstrap Logger) using configuration files
using Serilog;
using TTA.WebAPI;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateBootstrapLogger();

try
{
    Log.Information("TTA Application starting up...");

    var builder = WebApplication.CreateBuilder(args);

    // 2. Register application services via extension method
    builder.AddApplicationServices();

    var app = builder.Build();

    // 3. Setup middleware pipeline via extension method
    app.Configure();

    Log.Information("TTA Application has started successfully");

    // Changed to RunAsync to satisfy SonarCloud async-await requirements
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TTA Application terminated unexpectedly during startup");
}
finally
{
    Log.Information("TTA Application shut down complete");
    // Changed to CloseAndFlushAsync to ensure all logs are flushed properly
    await Log.CloseAndFlushAsync();
}

public partial class Program { }