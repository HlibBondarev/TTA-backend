using DbUp;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using Serilog.Exceptions;
using TTA.BusinessLogic.Services;
using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;
using TTA.DataAccess;
using TTA.DataAccess.Repository.Auth;
using TTA.DataAccess.Repository.Base;
using TTA.WebAPI.Authorization;
using TTA.WebAPI.Middleware;

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
        var configuration = builder.Configuration;

        // Get connection string from configuration (secrets.json)
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // Run DbUp migrations
        EnsureDatabaseUpsert(connectionString);

        // Authentication (Auth0)
        _ = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "JwtBearer";
            options.DefaultChallengeScheme = "JwtBearer";
        }).AddJwtBearer("JwtBearer", options =>
        {
            options.Authority = configuration["Auth0:Authority"];
            options.Audience = configuration["Auth0:Audience"];
        });

        // Authorization Policies
        services.AddAuthorization(options =>
        {
            // Add your custom policies here
            options.AddPolicy("ClubViewer", policy =>
                policy.Requirements.Add(new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club)));

            options.AddPolicy("ClubAdmin", policy =>
                policy.Requirements.Add(new ScopePermissionRequirement(AppRole.FullControl, TargetScope.Club)));

            options.AddPolicy("TeamEditor", policy =>
                policy.Requirements.Add(new ScopePermissionRequirement(AppRole.Editor, TargetScope.Team)));
        });

        // Registering HttpClient for CurrentUserService
        services.AddHttpClient<ICurrentUserService, CurrentUserService>(client =>
        {
            client.BaseAddress = new Uri(configuration["Auth0:Authority"]!);
        });

        services.AddScoped<IAccessRepository, AccessRepository>();
        services.AddScoped<IAccessService, AccessService>();

        // Handler must be registered as Singleton
        services.AddSingleton<IAuthorizationHandler, ScopePermissionHandler>();

        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

        services.AddControllers();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    /// <summary>
    /// Configures the HTTP request pipeline.
    /// </summary>
    public static void Configure(this WebApplication app)
    {
        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRouting();

        app.UseAuthentication(); // Who are you? (JWT check)
        app.UseAuthorization();  // Can you come here? (Policy check)

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