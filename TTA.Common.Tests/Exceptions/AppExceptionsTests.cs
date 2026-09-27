using TTA.Common.Exceptions;

namespace TTA.Common.Tests.Exceptions;

public class AppExceptionsTests
{
    [Fact]
    public void BadRequestException_ShouldSetCorrectMessageAndStatusCode()
    {
        // Arrange
        var message = "Custom bad request message";

        // Act
        var exception = new BadRequestException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void UnauthorizedException_ShouldSetCorrectMessageAndStatusCode()
    {
        // Arrange
        var message = "Custom unauthorized message";

        // Act
        var exception = new UnauthorizedException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public void ForbiddenException_ShouldSetCorrectMessageAndStatusCode()
    {
        // Arrange
        var message = "Custom forbidden message";

        // Act
        var exception = new ForbiddenException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(403, exception.StatusCode);
    }

    [Fact]
    public void NotFoundException_ShouldSetCorrectMessageAndStatusCode()
    {
        // Arrange
        var message = "Custom not found message";

        // Act
        var exception = new NotFoundException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public void Exceptions_ShouldUseDefaultMessage_WhenNoneProvided()
    {
        // Act & Assert
        Assert.Equal("Bad Request", new BadRequestException().Message);
        Assert.Equal("Unauthorized access", new UnauthorizedException().Message);
        Assert.Equal("Access forbidden", new ForbiddenException().Message);
        Assert.Equal("The requested resource was not found", new NotFoundException().Message);
    }
}