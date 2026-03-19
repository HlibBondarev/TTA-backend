using DbUp;
using Serilog;
using Serilog.Exceptions;
using TTA.DataAccess;

namespace TTA.WebAPI;

public static class Startup
{
    /// <summary>
    /// Registers application services into the DI container.
    /// </summary>
    public static void AddApplicationServices(this WebApplicationBuilder builder)
    {
        // Clear default providers to prevent duplication
        builder.Logging.ClearProviders();

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .Enrich.WithProcessId());
        // REMOVED .WriteTo.Console() here because it's already in appsettings.json

        var services = builder.Services;
        // Get connection string from configuration (secrets.json)
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

        // Run DbUp migrations
        EnsureDatabaseUpsert(connectionString);

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    /// <summary>
    /// Configures the HTTP request pipeline.
    /// </summary>
    public static void Configure(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();
        app.MapControllers();

        // Ensure all logs are written before the application exits
        app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
    }

    private static void EnsureDatabaseUpsert(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            Log.Error("Database connection string is missing. Skipping migrations.");
            return;
        }

        // Ensure the database exists
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        // Configure DbUp to look for scripts embedded in TTA.DataAccess project
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(Placeholder).Assembly)
            .LogToConsole() // Correct method name for standard console output
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Log.Fatal(result.Error, "Database upgrade failed");
            throw result.Error;
        }

        Log.Information("Database upgrade completed successfully");
    }
}