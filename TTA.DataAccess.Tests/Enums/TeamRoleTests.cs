using TTA.DataAccess.Enums;

namespace TTA.DataAccess.Tests.Enums;

public class TeamRoleTests
{
    [Fact]
    public void TeamRole_HasSevenMembers()
    {
        var values = Enum.GetValues<TeamRole>();
        Assert.Equal(7, values.Length);
    }

    [Theory]
    [InlineData(TeamRole.HeadCoach, 0)]
    [InlineData(TeamRole.AssistantCoach, 1)]
    [InlineData(TeamRole.ClubDirector, 2)]
    [InlineData(TeamRole.TeamManager, 3)]
    [InlineData(TeamRole.Analyst, 4)]
    [InlineData(TeamRole.Captain, 5)]
    [InlineData(TeamRole.Player, 6)]

    public void TeamRole_Members_HaveExpectedIntegerValues(TeamRole role, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)role);
    }

    [Theory]
    [InlineData("HeadCoach", TeamRole.HeadCoach)]
    [InlineData("AssistantCoach", TeamRole.AssistantCoach)]
    [InlineData("ClubDirector", TeamRole.ClubDirector)]
    [InlineData("TeamManager", TeamRole.TeamManager)]
    [InlineData("Analyst", TeamRole.Analyst)]
    [InlineData("Player", TeamRole.Player)]
    [InlineData("Captain", TeamRole.Captain)]
    public void TeamRole_CanBeParsedFromString(string name, TeamRole expectedRole)
    {
        var result = Enum.Parse<TeamRole>(name);
        Assert.Equal(expectedRole, result);
    }

    [Theory]
    [InlineData(TeamRole.HeadCoach, "HeadCoach")]
    [InlineData(TeamRole.AssistantCoach, "AssistantCoach")]
    [InlineData(TeamRole.ClubDirector, "ClubDirector")]
    [InlineData(TeamRole.TeamManager, "TeamManager")]
    [InlineData(TeamRole.Analyst, "Analyst")]
    [InlineData(TeamRole.Player, "Player")]
    [InlineData(TeamRole.Captain, "Captain")]
    public void TeamRole_ToString_ReturnsExpectedName(TeamRole role, string expectedName)
    {
        Assert.Equal(expectedName, role.ToString());
    }

    [Fact]
    public void TeamRole_AllValuesAreDefined()
    {
        foreach (TeamRole role in Enum.GetValues<TeamRole>())
        {
            Assert.True(Enum.IsDefined(role));
        }
    }

    [Fact]
    public void TeamRole_UndefinedValue_IsNotDefined()
    {
        Assert.False(Enum.IsDefined(typeof(TeamRole), 99));
    }
}