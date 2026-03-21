using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.ComponentModel.DataAnnotations;
using System.Security.Authentication;
using System.Text.Json;
using TTA.Common.Exceptions;
using TTA.WebAPI.Middleware;

namespace TTA.WebAPI.Tests.Middleware;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _loggerMock;
    private readonly GlobalExceptionHandler _handler;
    private readonly DefaultHttpContext _context;

    public GlobalExceptionHandlerTests()
    {
        _loggerMock = new Mock<ILogger<GlobalExceptionHandler>>();
        _handler = new GlobalExceptionHandler(_loggerMock.Object);
        _context = new DefaultHttpContext();

        // Setup a memory stream to capture the response body
        _context.Response.Body = new MemoryStream();
    }

    [Theory]
    [InlineData(typeof(ArgumentNullException), StatusCodes.Status400BadRequest)]
    [InlineData(typeof(KeyNotFoundException), StatusCodes.Status404NotFound)]
    [InlineData(typeof(UnauthorizedAccessException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(AuthenticationException), StatusCodes.Status401Unauthorized)]
    public async Task TryHandleAsync_ShouldMapExceptionsToCorrectStatusCodes(Type exceptionType, int expectedStatusCode)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        // Act
        var result = await _handler.TryHandleAsync(_context, exception, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedStatusCode, _context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldHandleBaseException_WithCustomStatusCode()
    {
        // Arrange
        var exception = new BadRequestException("Custom error message");

        // Act
        await _handler.TryHandleAsync(_context, exception, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, _context.Response.StatusCode);

        var response = await GetProblemDetailsFromResponse();
        Assert.Equal("Custom error message", response.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldFormatValidationExceptionData_WhenPresent()
    {
        // Arrange
        var exception = new ValidationException("Validation failed");
        exception.Data.Add("Email", new[] { "Invalid format", "Too short" });

        // Act
        await _handler.TryHandleAsync(_context, exception, CancellationToken.None);

        // Assert
        var response = await GetProblemDetailsFromResponse();
        Assert.Contains("Email: Invalid format, Too short", response.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldIncludeTraceIdInResponse()
    {
        // Arrange
        var exception = new Exception("Generic error");
        _context.TraceIdentifier = "test-trace-id";

        // Act
        await _handler.TryHandleAsync(_context, exception, CancellationToken.None);

        // Assert
        var response = await GetProblemDetailsFromResponse();
        Assert.True(response.Extensions.ContainsKey("traceId"));
        Assert.Equal("test-trace-id", response.Extensions["traceId"]?.ToString());
    }

    private async Task<ProblemDetails> GetProblemDetailsFromResponse()
    {
        _context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(_context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ProblemDetails>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    }
}
