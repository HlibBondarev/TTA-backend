using Microsoft.AspNetCore.Http;
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
    private readonly DefaultHttpContext _context;

    // Fix for Sonar: Avoid creating a new JsonSerializerOptions instance for every operation
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Fix for Sonar: Use static readonly fields for constant array arguments
    private static readonly string[] EmailErrors = ["Invalid format", "Too short"];

    public GlobalExceptionHandlerTests()
    {
        _loggerMock = new Mock<ILogger<GlobalExceptionHandler>>();
        _handler = new GlobalExceptionHandler(_loggerMock.Object);
        _context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
    }

    [Fact]
    public async Task TryHandleAsync_ShouldFormatValidationExceptionData_WhenPresent()
    {
        // Arrange
        var exception = new ValidationException("Validation failed");
        exception.Data.Add("Email", EmailErrors);

        // Act
        await _handler.TryHandleAsync(_context, exception, CancellationToken.None);

        // Assert
        var response = await GetProblemDetailsFromResponse();
        Assert.Contains("Email: Invalid format, Too short", response.Detail);
    }

    private async Task<ProblemDetails> GetProblemDetailsFromResponse()
    {
        _context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(_context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ProblemDetails>(body, JsonOptions)!;
    }

    [Theory]
    // System exceptions should now map to 500 (Internal Server Error)
    [InlineData(typeof(ArgumentNullException), StatusCodes.Status500InternalServerError)]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError)]
    // Specifically mapped framework exceptions
    [InlineData(typeof(KeyNotFoundException), StatusCodes.Status404NotFound)]
    [InlineData(typeof(UnauthorizedAccessException), StatusCodes.Status403Forbidden)]
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

    [Fact]
    public async Task TryHandleAsync_ShouldMapBaseExceptionToCustomStatusCode()
    {
        // Arrange
        // Assuming you have a BadRequestException inherited from BaseException
        var exception = new BadRequestException("Client side error");

        // Act
        var result = await _handler.TryHandleAsync(_context, exception, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.Equal(StatusCodes.Status400BadRequest, _context.Response.StatusCode);
    }
}
