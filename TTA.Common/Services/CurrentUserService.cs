using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using TTA.Common.Extensions;
using TTA.Common.Services.Api;
using TTA.Common.Services.DTOs;

namespace TTA.Common.Services;

/// <summary>
/// Implementation of the current user service using an HttpClient to communicate with a 'userinfo' endpoint.
/// </summary>
public class CurrentUserService(HttpClient httpClient) : ICurrentUserService
{
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc />
    public async Task<UserFromClaimsDto> GetUserPropertiesFromClaims(string authorizationHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "userinfo");
        request.Headers.Add("Authorization", authorizationHeader);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new UnauthorizedAccessException("The user is not authenticated.");
        }

        // Using our previously created extension for JSON options
        var options = new JsonSerializerOptions().GetDefault();

        return (await response.Content.ReadFromJsonAsync<UserFromClaimsDto>(options)) ??
               throw new AuthenticationException("Cannot get user's claims from context.");
    }
}
