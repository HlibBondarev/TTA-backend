namespace TTA.WebAPI.Authorization;

public static class Auth0ConfigHelper
{
    /// <summary>
    /// Validates and returns Auth0 settings from configuration.
    /// Throws InvalidOperationException if any required setting is missing.
    /// </summary>
    public static Auth0Settings GetRequiredAuth0Settings(IConfiguration config)
    {
        // 1. Get the raw value first
        var rawAuthority = config["Auth0:Authority"];

        // 2. Check if it's missing BEFORE adding the slash
        if (string.IsNullOrWhiteSpace(rawAuthority))
        {
            throw new InvalidOperationException("Auth0:Authority is missing in configuration.");
        }

        // 3. Now it's safe to normalize
        var authority = rawAuthority.TrimEnd('/') + "/";

        var clientId = config["Auth0:ClientId"]
            ?? throw new InvalidOperationException("Auth0:ClientId is missing in configuration.");

        var audience = config["Auth0:Audience"]
            ?? throw new InvalidOperationException("Auth0:Audience is missing in configuration.");

        return new Auth0Settings(authority, clientId, audience);
    }
}