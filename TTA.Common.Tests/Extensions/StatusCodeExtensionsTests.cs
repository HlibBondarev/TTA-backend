using Microsoft.AspNetCore.Http;
using TTA.Common.Extensions;

namespace TTA.Common.Tests.Extensions;

public class StatusCodeExtensionsTests
{
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "Bad Request")]
    [InlineData(StatusCodes.Status401Unauthorized, "Unauthorized")]
    [InlineData(StatusCodes.Status403Forbidden, "Forbidden")]
    [InlineData(StatusCodes.Status404NotFound, "Not Found")]
    [InlineData(StatusCodes.Status500InternalServerError, "Internal Server Error")]
    [InlineData(405, "Status 405")] // New test case for neutral representation
    [InlineData(200, "Status 200")] // New test case for neutral representation
    public void GetTitleForStatus_ShouldReturnExpectedTitle(int statusCode, string expectedTitle)
    {
        // Act
        var result = statusCode.GetTitleForStatus();

        // Assert
        Assert.Equal(expectedTitle, result);
    }
}
