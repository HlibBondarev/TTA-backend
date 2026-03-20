using TTA.DataAccess.Enums;

namespace TTA.WebAPI.Tests.Enums;

public class TimeAnchorTypeTests
{
    [Fact]
    public void TimeAnchorType_HasFourMembers()
    {
        var values = Enum.GetValues<TimeAnchorType>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(TimeAnchorType.PeriodStart, 0)]
    [InlineData(TimeAnchorType.PeriodEnd, 1)]
    [InlineData(TimeAnchorType.StoppageStart, 2)]
    [InlineData(TimeAnchorType.StoppageEnd, 3)]
    public void TimeAnchorType_Members_HaveExpectedIntegerValues(TimeAnchorType type, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)type);
    }

    [Theory]
    [InlineData("PeriodStart", TimeAnchorType.PeriodStart)]
    [InlineData("PeriodEnd", TimeAnchorType.PeriodEnd)]
    [InlineData("StoppageStart", TimeAnchorType.StoppageStart)]
    [InlineData("StoppageEnd", TimeAnchorType.StoppageEnd)]
    public void TimeAnchorType_CanBeParsedFromString(string name, TimeAnchorType expectedType)
    {
        var result = Enum.Parse<TimeAnchorType>(name);
        Assert.Equal(expectedType, result);
    }

    [Theory]
    [InlineData(TimeAnchorType.PeriodStart, "PeriodStart")]
    [InlineData(TimeAnchorType.PeriodEnd, "PeriodEnd")]
    [InlineData(TimeAnchorType.StoppageStart, "StoppageStart")]
    [InlineData(TimeAnchorType.StoppageEnd, "StoppageEnd")]
    public void TimeAnchorType_ToString_ReturnsExpectedName(TimeAnchorType type, string expectedName)
    {
        Assert.Equal(expectedName, type.ToString());
    }

    [Fact]
    public void TimeAnchorType_AllValuesAreDefined()
    {
        foreach (TimeAnchorType type in Enum.GetValues<TimeAnchorType>())
        {
            Assert.True(Enum.IsDefined(typeof(TimeAnchorType), type));
        }
    }

    [Fact]
    public void TimeAnchorType_UndefinedValue_IsNotDefined()
    {
        Assert.False(Enum.IsDefined(typeof(TimeAnchorType), 99));
    }

    [Fact]
    public void TimeAnchorType_StoppageValues_AreGreaterThanPeriodValues()
    {
        Assert.True((int)TimeAnchorType.StoppageStart > (int)TimeAnchorType.PeriodEnd);
        Assert.True((int)TimeAnchorType.StoppageEnd > (int)TimeAnchorType.StoppageStart);
    }
}