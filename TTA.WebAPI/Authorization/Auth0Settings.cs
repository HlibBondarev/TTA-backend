/// <summary>
/// Represents Auth0 configuration settings required for authentication and custom claim extraction.
/// </summary>
/// <param name="Authority">The Auth0 domain URL.</param>
/// <param name="ClientId">The Auth0 Client ID.</param>
/// <param name="Audience">The API Identifier (Audience).</param>
/// <param name="Namespace">The custom namespace used for custom claims in the Access Token.</param>
public record Auth0Settings(string Authority, string ClientId, string Audience, string Namespace)
{
    /// <summary>
    /// Gets the computed authorization URL.
    /// </summary>
    public string AuthorizationUrl => $"{Authority}authorize";

    /// <summary>
    /// Gets the computed token exchange URL.
    /// </summary>
    public string TokenUrl => $"{Authority}oauth/token";
}