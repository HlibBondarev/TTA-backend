using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using TTA.Common.Exceptions;
using TTA.WebAPI.Middleware;

namespace TTA.WebAPI.Tests.Middleware;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _loggerMock;
    private readonly GlobalExceptionHandler _handler;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] EmailErrors = ["Invalid format", "Too short"];

    public GlobalExceptionHandlerTests()
    {
        _loggerMock = new Mock<ILogger<GlobalExceptionHandler>>();
        _handler = new GlobalExceptionHandler(_loggerMock.Object);
    }

    private static DefaultHttpContext CreateFreshContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "test-trace-id";
        return context;
    }

    private static async Task<ProblemDetails> GetProblemDetailsFromResponse(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ProblemDetails>(body, JsonOptions)!;
    }

    [Fact]
    public async Task TryHandleAsync_ShouldReturnFalse_WhenResponseHasAlreadyStarted()
    {
        // Arrange
        var context = CreateFreshContext();
        var exception = new Exception("Late error");

        // Mock the IHttpResponseFeature to return HasStarted = true
        var responseFeatureMock = new Mock<IHttpResponseFeature>();
        responseFeatureMock.Setup(f => f.HasStarted).Returns(true);
        context.Features.Set(responseFeatureMock.Object);

        // Act
        var result = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(typeof(ArgumentNullException), StatusCodes.Status500InternalServerError)]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError)]
    [InlineData(typeof(KeyNotFoundException), StatusCodes.Status404NotFound)]
    [InlineData(typeof(UnauthorizedAccessException), StatusCodes.Status401Unauthorized)]
    public async Task TryHandleAsync_ShouldMapExceptionsToCorrectStatusCodes(Type exceptionType, int expectedStatusCode)
    {
        // Arrange
        var context = CreateFreshContext();
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        // Act
        var result = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldIncludeTraceIdInResponse()
    {
        // Arrange
        var context = CreateFreshContext();
        var exception = new Exception("Generic error");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        var response = await GetProblemDetailsFromResponse(context);
        Assert.True(response.Extensions.ContainsKey("traceId"));
        Assert.Equal("test-trace-id", response.Extensions["traceId"]?.ToString());
    }

    [Fact]
    public async Task TryHandleAsync_ShouldFormatValidationExceptionData_WhenPresent()
    {
        // Arrange
        var context = CreateFreshContext();
        var exception = new ValidationException("Validation failed");
        exception.Data.Add("Email", EmailErrors);

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        var response = await GetProblemDetailsFromResponse(context);

        // 1. Check the flattened string in Detail
        Assert.Contains("Email: Invalid format, Too short", response.Detail);

        // 2. Check the structured dictionary in Extensions (Nitpick fix)
        Assert.True(response.Extensions.ContainsKey("errors"));
        var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            response.Extensions["errors"]!.ToString()!, JsonOptions);

        Assert.NotNull(errors);
        Assert.True(errors.ContainsKey("Email"));
        Assert.Equal(EmailErrors, errors["Email"]);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldHandleBaseException_WithCustomStatusCode()
    {
        // Arrange
        var context = CreateFreshContext();
        var exception = new BadRequestException("Custom client error");

        // Act
        // Consolidated: asserting both the return value and the status code in one test
        var result = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(result); // Added as per CodeRabbit's suggestion
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        var response = await GetProblemDetailsFromResponse(context);
        Assert.Equal("Custom client error", response.Detail);
    }
}