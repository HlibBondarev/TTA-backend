using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using TTA.Common.Services;
using TTA.Common.Services.DTOs;

namespace TTA.Common.Tests.Services;

public class CurrentUserServiceTests
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly CurrentUserService _service;

    public CurrentUserServiceTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.identity.com/")
        };
        _service = new CurrentUserService(_httpClient);
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldReturnUser_WhenResponseIsSuccessful()
    {
        // Arrange
        var expectedUser = new UserFromClaimsDto("user-123", "test@example.com", "John Doe");
        var authHeader = "Bearer token123";

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.Headers.Authorization != null &&
                    req.Headers.Authorization.ToString() == authHeader),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedUser)
            });

        // Act
        var result = await _service.GetUserPropertiesFromClaims(authHeader);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedUser.Id, result.Id);
        Assert.Equal(expectedUser.Email, result.Email);
        Assert.Equal(expectedUser.Name, result.Name);
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldThrowUnauthorizedAccessException_WhenStatusIs401()
    {
        // Arrange
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetUserPropertiesFromClaims("invalid-token"));
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldThrowAuthenticationException_WhenContentIsNull()
    {
        // Arrange
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                // "null" is a valid JSON that will make ReadFromJsonAsync return null
                Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
            });

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            _service.GetUserPropertiesFromClaims("token"));
    }
}
