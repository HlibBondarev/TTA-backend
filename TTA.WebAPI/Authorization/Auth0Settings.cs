namespace TTA.WebAPI.Authorization;

public record Auth0Settings(string Authority, string ClientId, string Audience)
{
    // Computed properties to ensure URLs are constructed correctly
    public string AuthorizationUrl => $"{Authority}authorize";
    public string TokenUrl => $"{Authority}oauth/token";
}