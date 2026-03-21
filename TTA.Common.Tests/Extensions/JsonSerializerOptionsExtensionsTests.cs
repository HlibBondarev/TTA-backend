using System.Text.Json;
using System.Text.Json.Serialization;
using TTA.Common.Extensions;

namespace TTA.Common.Tests.Extensions;

public class JsonSerializerOptionsExtensionsTests
{
    [Fact]
    public void GetDefault_ShouldConfigureOptionsCorrectly()
    {
        // Arrange
        var options = new JsonSerializerOptions();

        // Act
        var result = options.GetDefault();

        // Assert
        Assert.True(result.PropertyNameCaseInsensitive);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, result.DefaultIgnoreCondition);
        Assert.Equal(JsonNumberHandling.AllowReadingFromString, result.NumberHandling);
    }

    [Fact]
    public void GetDefault_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        // Arrange
        JsonSerializerOptions? options = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => options!.GetDefault());
    }

    [Fact]
    public void GetDefault_ShouldReturnSameInstance()
    {
        // Arrange
        var options = new JsonSerializerOptions();

        // Act
        var result = options.GetDefault();

        // Assert
        Assert.Same(options, result);
    }
}