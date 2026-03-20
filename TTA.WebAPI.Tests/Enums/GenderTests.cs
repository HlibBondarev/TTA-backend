using TTA.DataAccess.Enums;

namespace TTA.WebAPI.Tests.Enums;

public class GenderTests
{
    [Fact]
    public void Gender_HasExpectedValues()
    {
        var values = Enum.GetValues<Gender>();
        Assert.Equal(2, values.Length);
    }

    [Fact]
    public void Gender_Male_HasValueZero()
    {
        Assert.Equal(0, (int)Gender.Male);
    }

    [Fact]
    public void Gender_Female_HasValueOne()
    {
        Assert.Equal(1, (int)Gender.Female);
    }

    [Fact]
    public void Gender_Male_CanBeParsedFromString()
    {
        var result = Enum.Parse<Gender>("Male");
        Assert.Equal(Gender.Male, result);
    }

    [Fact]
    public void Gender_Female_CanBeParsedFromString()
    {
        var result = Enum.Parse<Gender>("Female");
        Assert.Equal(Gender.Female, result);
    }

    [Theory]
    [InlineData(Gender.Male, "Male")]
    [InlineData(Gender.Female, "Female")]
    public void Gender_ToString_ReturnsExpectedName(Gender gender, string expectedName)
    {
        Assert.Equal(expectedName, gender.ToString());
    }

    [Fact]
    public void Gender_ContainsMale()
    {
        Assert.True(Enum.IsDefined(typeof(Gender), Gender.Male));
    }

    [Fact]
    public void Gender_ContainsFemale()
    {
        Assert.True(Enum.IsDefined(typeof(Gender), Gender.Female));
    }

    [Fact]
    public void Gender_UndefinedValue_IsNotDefined()
    {
        Assert.False(Enum.IsDefined(typeof(Gender), 99));
    }
}