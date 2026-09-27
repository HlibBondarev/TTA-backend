using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using TTA.BusinessLogic.Services.Api;
using TTA.BusinessLogic.Services.DTOs;
using TTA.Common.Extensions;

namespace TTA.BusinessLogic.Services;

/// <summary>
/// Implementation of the current user service using an HttpClient to communicate with a 'userinfo' endpoint.
/// </summary>
public class CurrentUserService(HttpClient httpClient) : ICurrentUserService
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<UserFromClaimsDto> GetUserPropertiesFromClaims(string authorizationHeader, CancellationToken cancellationToken = default)
    {
        // Use 'using' declaration to ensure the request is disposed after the method execution
        using var request = new HttpRequestMessage(HttpMethod.Get, "userinfo");

        request.Headers.Add("Authorization", authorizationHeader);

        // Propagate CancellationToken to the async HTTP call
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // Specific handling for different status codes to improve diagnostics
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException("The access token is invalid or expired.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException("The user does not have permission to access this resource.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Identity provider returned an unexpected status code: {response.StatusCode}");
        }

        var options = new JsonSerializerOptions().GetDefault();

        // Pass cancellationToken to the JSON deserialization process
        return await response.Content.ReadFromJsonAsync<UserFromClaimsDto>(options, cancellationToken) ??
               throw new AuthenticationException("Failed to deserialize user properties from the identity provider response.");
    }
}
