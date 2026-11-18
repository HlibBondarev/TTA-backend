using System.Text.Json;
using System.Text.Json.Serialization;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;

namespace TTA.DataAccess.Tests.Enums;

/// <summary>
/// Unit tests for the <see cref="FoulImpact"/> enum and its default usage on <see cref="EventDefinition"/>.
/// </summary>
public class FoulImpactTests
{
    [Fact]
    public void FoulImpact_HasFourMembers()
    {
        var values = Enum.GetValues<FoulImpact>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(FoulImpact.None, 0)]
    [InlineData(FoulImpact.PersonalFoul, 1)]
    [InlineData(FoulImpact.DisqualificationWithSub, 2)]
    [InlineData(FoulImpact.DisqualificationNoSub, 3)]
    public void FoulImpact_Members_HaveExpectedIntegerValues(FoulImpact impact, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)impact);
    }

    [Fact]
    public void FoulImpact_DefaultValue_IsNone()
    {
        Assert.Equal(FoulImpact.None, default(FoulImpact));
    }

    [Fact]
    public void EventDefinition_FoulImpact_DefaultsToNone_AndPenaltyDurationToNull()
    {
        var definition = new EventDefinition();

        Assert.Equal(FoulImpact.None, definition.FoulImpact);
        Assert.Null(definition.PenaltyDurationSeconds);
    }

    [Theory]
    [InlineData(FoulImpact.None, "\"None\"")]
    [InlineData(FoulImpact.PersonalFoul, "\"PersonalFoul\"")]
    [InlineData(FoulImpact.DisqualificationWithSub, "\"DisqualificationWithSub\"")]
    [InlineData(FoulImpact.DisqualificationNoSub, "\"DisqualificationNoSub\"")]
    public void FoulImpact_SerializesAsString_WhenJsonStringEnumConverterIsRegistered(FoulImpact impact, string expectedJson)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        var json = JsonSerializer.Serialize(impact, options);

        Assert.Equal(expectedJson, json);
    }

    [Theory]
    [InlineData("\"PersonalFoul\"", FoulImpact.PersonalFoul)]
    [InlineData("\"DisqualificationNoSub\"", FoulImpact.DisqualificationNoSub)]
    public void FoulImpact_DeserializesFromString_WhenJsonStringEnumConverterIsRegistered(string json, FoulImpact expected)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        var result = JsonSerializer.Deserialize<FoulImpact>(json, options);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void FoulImpact_UndefinedValue_IsNotDefined(int value)
    {
        Assert.False(Enum.IsDefined(typeof(FoulImpact), value));
    }

    [Fact]
    public void SportConfiguration_MaxPersonalFouls_DefaultsToNull()
    {
        var configuration = new SportConfiguration();

        Assert.Null(configuration.MaxPersonalFouls);
    }
}
