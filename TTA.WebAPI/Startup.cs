using DbUp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
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

        // Registering the handler with Scoped lifetime (to resolve IAccessService correctly)
        builder.Services.AddScoped<IAuthorizationHandler, ScopePermissionHandler>();

        // Defining policies using the modern AuthorizationBuilder
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("ClubViewer", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ScopePermissionRequirement(AppRole.Viewer, TargetScope.Club));
            })
            .AddPolicy("ClubAdmin", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ScopePermissionRequirement(AppRole.FullControl, TargetScope.Club));
            })
            .AddPolicy("TeamEditor", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ScopePermissionRequirement(AppRole.Editor, TargetScope.Team));
            });

        // Registering HttpClient for CurrentUserService
        services.AddHttpClient<ICurrentUserService, CurrentUserService>(client =>
        {
            client.BaseAddress = new Uri(configuration["Auth0:Authority"]!);
        });

        services.AddScoped<IAccessRepository, AccessRepository>();
        services.AddScoped<IAccessService, AccessService>();

        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

        services.AddControllers();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();

        // Swagger Configuration with OAuth2 Client Credentials Flow
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        // Clean URLs without query parameters
                        AuthorizationUrl = new Uri($"{builder.Configuration["Auth0:Authority"]}authorize"),
                        TokenUrl = new Uri($"{builder.Configuration["Auth0:Authority"]}oauth/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            { "openid", "OpenID Profile" },
                            { "profile", "User Profile" },
                            { "email", "User Email" }
                        }
                    }
                }
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
                    },
                    new[] { "openid", "profile", "email" }
                }
            });
        });
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
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "TTA API V1");

                // Public Client ID is safe for the browser
                options.OAuthClientId(app.Configuration["Auth0:ClientId"]);

                // REMOVED: OAuthClientSecret(app.Configuration["Auth0:ClientSecret"]) 
                // Confidential secrets must never be exposed to the browser UI.

                // PKCE must remain enabled to handle secure code exchange without a secret
                options.OAuthUsePkce();

                options.OAuthAdditionalQueryStringParams(new Dictionary<string, string>
                {
                    { "audience", app.Configuration["Auth0:Audience"]! }
                });
            });
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