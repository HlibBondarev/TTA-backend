using TTA.Common.Extensions;

namespace TTA.Common.Tests.Extensions;

public class CollectionExtensionsTests
{
    [Fact]
    public void IsNullOrEmpty_ShouldReturnTrue_WhenCollectionIsNull()
    {
        // Arrange
        IEnumerable<string>? list = null;

        // Act
        var result = list.IsNullOrEmpty();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsNullOrEmpty_ShouldReturnTrue_WhenCollectionIsEmpty()
    {
        // Arrange
        var list = new List<int>();

        // Act
        var result = list.IsNullOrEmpty();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsNullOrEmpty_ShouldReturnFalse_WhenCollectionHasElements()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act
        var result = list.IsNullOrEmpty();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void MapToList_ShouldTransformElementsCorrectly()
    {
        // Arrange
        var source = new List<int> { 1, 2, 3 };

        // Act
        var result = source.MapToList(x => x.ToString());

        // Assert
        Assert.IsType<List<string>>(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("1", result[0]);
        Assert.Equal("3", result[2]);
    }
}
