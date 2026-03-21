using TTA.Common.Services.DTOs;

namespace TTA.Common.Services.Api;

/// <summary>
/// Defines a service to retrieve current user information from identity providers.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets user properties from the identity provider using the provided authorization header.
    /// </summary>
    /// <param name="authorizationHeader">The raw "Authorization" header from the request.</param>
    /// <returns>A DTO containing user claims.</returns>
    Task<UserFromClaimsDto> GetUserPropertiesFromClaims(string authorizationHeader);
}
