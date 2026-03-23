using Moq;
using Moq.Protected;
using System.Net;
using TTA.BusinessLogic.Services;

namespace TTA.BusinessLogic.Tests.Services;

public class CurrentUserServiceTests
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly CurrentUserService _service;

    public CurrentUserServiceTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://dev-qjg8tcy86dt7zrfv.us.auth0.com/")
        };
        _service = new CurrentUserService(_httpClient);
    }

    /// <summary>
    /// 1. Tests specific handling for HttpStatusCode.Unauthorized (401)
    /// </summary>
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

    /// <summary>
    /// 2. Tests specific handling for HttpStatusCode.Forbidden (403)
    /// </summary>
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

    /// <summary>
    /// 3. Tests general handling for other non-success status codes (e.g., 500)
    /// </summary>
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
}