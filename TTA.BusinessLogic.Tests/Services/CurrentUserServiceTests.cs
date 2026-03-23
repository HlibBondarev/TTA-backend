using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using TTA.BusinessLogic.Services;
using TTA.BusinessLogic.Services.DTOs;

namespace TTA.BusinessLogic.Tests.Services;

public class CurrentUserServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly CurrentUserService _service;

    public CurrentUserServiceTests()
    {
        // Using Loose behavior to avoid MockException during HttpClient.Dispose()
        _handlerMock = new Mock<HttpMessageHandler>();

        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.auth-test.com/")
        };
        _service = new CurrentUserService(_httpClient);
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldReturnUser_WhenTokenIsValid()
    {
        // Arrange
        var accessToken = "valid-token";
        var expectedUser = new UserFromClaimsDto("auth0|123", "test@example.com", "Test User");

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.Headers.Authorization != null &&
                    req.Headers.Authorization.Parameter == accessToken),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedUser)
            });

        // Act
        var result = await _service.GetUserPropertiesFromClaims($"Bearer {accessToken}");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedUser.Id, result.Id);
        Assert.Equal(expectedUser.Email, result.Email);

        _handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldThrowUnauthorizedAccess_WhenStatusIs401()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.Unauthorized });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetUserPropertiesFromClaims("Bearer invalid_token"));

        Assert.Equal("The access token is invalid or expired.", ex.Message);
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldThrowUnauthorizedAccess_WhenStatusIs403()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.Forbidden });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetUserPropertiesFromClaims("Bearer forbidden_token"));

        Assert.Equal("The user does not have permission to access this resource.", ex.Message);
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldThrowHttpRequestException_WhenStatusIs500()
    {
        // Arrange
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _service.GetUserPropertiesFromClaims("Bearer token"));

        Assert.Contains("Identity provider returned an unexpected status code: InternalServerError", ex.Message);
    }

    [Fact]
    public async Task GetUserPropertiesFromClaims_ShouldThrowAuthenticationException_WhenDeserializationReturnsNull()
    {
        // Arrange
        // The JSON token "null" deserializes to a null object.
        // It passes format validation but fails subsequent null-object integrity checks.
        var responseContent = new StringContent("null", System.Text.Encoding.UTF8, "application/json");

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = responseContent
            });

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            _service.GetUserPropertiesFromClaims("Bearer valid-token"));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}