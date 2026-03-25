namespace TTA.WebAPI.Authorization;

public static class Auth0ConfigHelper
{
    /// <summary>
    /// Validates and returns Auth0 settings from configuration.
    /// Throws InvalidOperationException if any required setting is missing.
    /// </summary>
    public static Auth0Settings GetRequiredAuth0Settings(IConfiguration config)
    {
        // Normalize authority by ensuring it ends with a single trailing slash
        var authority = config["Auth0:Authority"]?.TrimEnd('/') + "/"
            ?? throw new InvalidOperationException("Auth0:Authority is missing in configuration.");

        var clientId = config["Auth0:ClientId"]
            ?? throw new InvalidOperationException("Auth0:ClientId is missing in configuration.");

        var audience = config["Auth0:Audience"]
            ?? throw new InvalidOperationException("Auth0:Audience is missing in configuration.");

        return new Auth0Settings(authority, clientId, audience);
    }
}