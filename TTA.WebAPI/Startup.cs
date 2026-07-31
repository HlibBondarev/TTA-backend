using Dapper;
using DbUp;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;
using Serilog.Exceptions;
using System.Text.Json.Serialization;
using TTA.BusinessLogic;
using TTA.BusinessLogic.Services;
using TTA.BusinessLogic.Services.Api;
using TTA.Common.Enums;
using TTA.DataAccess;
using TTA.DataAccess.Infrastructure;
using TTA.DataAccess.Repository;
using TTA.DataAccess.Repository.Api;
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

        // 1. Retrieve the base connection string from configuration (secrets.json)
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 2. Prevent credentials drift between docker-compose/env variables and secrets.json
        // Isolate this shift to ignore the "Testing" environment so it never interferes with Testcontainers
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            var postgresUser = Environment.GetEnvironmentVariable("POSTGRES_USER");
            var postgresPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
            var postgresDb = Environment.GetEnvironmentVariable("POSTGRES_DB");

            var hasPostgresUser = !string.IsNullOrWhiteSpace(postgresUser);
            var hasPostgresPassword = !string.IsNullOrWhiteSpace(postgresPassword);
            var hasPostgresDb = !string.IsNullOrWhiteSpace(postgresDb);
            var hasPostgresOverride = hasPostgresUser || hasPostgresPassword || hasPostgresDb;
            var hasCompletePostgresOverride = hasPostgresUser && hasPostgresPassword && hasPostgresDb;

            // Enforce all-or-nothing override constraint to prevent mixed credential states
            if (hasPostgresOverride && !hasCompletePostgresOverride)
            {
                throw new InvalidOperationException(
                    "POSTGRES_USER, POSTGRES_PASSWORD, and POSTGRES_DB environment variables must be provided together as a complete set.");
            }

            // If a complete environment override set is available, replace base credentials cleanly
            if (hasCompletePostgresOverride)
            {
                var baseConnectionString = string.IsNullOrEmpty(connectionString)
                    ? "Host=localhost;Port=5432;"
                    : connectionString;

                // Use NpgsqlConnectionStringBuilder for robust and case-insensitive string manipulation
                var npgsqlBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
                {
                    Username = postgresUser,
                    Password = postgresPassword,
                    Database = postgresDb
                };

                connectionString = npgsqlBuilder.ConnectionString;

                // Overwrite the configuration value inline so that NpgsqlConnectionFactory 
                // and DbUp inside EnsureDatabaseUpsert resolve the exact same synchronized string
                configuration["ConnectionStrings:DefaultConnection"] = connectionString;
            }
        }

        // Run DbUp migrations
        EnsureDatabaseUpsert(connectionString);

        // Retrieve and validate settings at startup
        var auth0Settings = Auth0ConfigHelper.GetRequiredAuth0Settings(configuration);

        // Register Auth0Settings as a Singleton in the DI container
        // This allows injecting it directly into Controllers or Services
        services.AddSingleton(auth0Settings);

        // Authentication (Auth0)
        _ = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "JwtBearer";
            options.DefaultChallengeScheme = "JwtBearer";
        }).AddJwtBearer("JwtBearer", options =>
        {
            options.Authority = auth0Settings.Authority;
            options.Audience = auth0Settings.Audience;
        });

        // Registering the handler with Scoped lifetime (to resolve IAccessService correctly)
        builder.Services.AddScoped<IAuthorizationHandler, ScopePermissionHandler>();

        // Reference the assembly via the stable Placeholder class
        var businessLogicAssembly = typeof(BusinessLogicPlaceholder).Assembly;

        // Register MediatR
        builder.Services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(businessLogicAssembly));

        // Register all Validators from BusinessLogic assembly
        // This allows injecting IValidator<CreateClubRequest> into controllers
        builder.Services.AddValidatorsFromAssembly(businessLogicAssembly);

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
            })
            .AddPolicy("TeamAdmin", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ScopePermissionRequirement(AppRole.FullControl, TargetScope.Team));
            });

        // Registering HttpClient for CurrentUserService
        services.AddHttpClient<ICurrentUserService, CurrentUserService>(client =>
        {
            client.BaseAddress = new Uri(auth0Settings.Authority);
        });

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

        services.AddScoped<IAccessRepository, AccessRepository>();
        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<ITimeNormalizationService, TimeNormalizationService>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IClubRepository, ClubRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITeamMembershipRepository, TeamMembershipRepository>();
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<IRosterRepository, RosterRepository>();
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<IMatchLineupRepository, MatchLineupRepository>();
        services.AddScoped<IGameEventRepository, GameEventRepository>();
        services.AddScoped<ITimeAnchorRepository, TimeAnchorRepository>();
        services.AddScoped<IPlayerPresenceRepository, PlayerPresenceRepository>();
        services.AddScoped<IEventDefinitionRepository, EventDefinitionRepository>();
        services.AddScoped<ISportRepository, SportRepository>();
        services.AddScoped<ISportConfigurationRepository, SportConfigurationRepository>();

        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

        // Add services to the container.
        services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Global configuration to serialize/deserialize Enums as strings (e.g., "ClubDirector") 
            // instead of numeric values (e.g., 0). This improves API readability and Swagger UI integration.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();

        // Swagger configuration for OAuth2 Authorization Code flow with PKCE
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        // Use normalized URIs from the settings record
                        AuthorizationUrl = new Uri(auth0Settings.AuthorizationUrl),
                        TokenUrl = new Uri(auth0Settings.TokenUrl),
                        Scopes = new Dictionary<string, string>
                        {
                            { "openid", "OpenID" },
                            { "profile", "Profile" },
                            { "email", "Email" }
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

                // Pre-select checkboxes for the specified scopes in the authorization modal
                options.OAuthScopes("openid", "profile", "email");

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
            .WithScriptsEmbeddedInAssembly(typeof(SqlPlaceholder).Assembly)
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