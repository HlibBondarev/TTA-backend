
using TTA.BusinessLogic.Services.DTOs;

namespace TTA.BusinessLogic.Services.Api;

/// <summary>
/// Defines a service for retrieving current user properties based on security claims.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Retrieves user properties from the identity provider using the provided authorization header.
    /// </summary>
    /// <param name="authorizationHeader">The raw 'Authorization' header containing the access token.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="UserFromClaimsDto"/> containing user information if successful.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the token is invalid or expired.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">Thrown when user data cannot be parsed.</exception>
    Task<UserFromClaimsDto> GetUserPropertiesFromClaims(string authorizationHeader, CancellationToken cancellationToken = default);
}
