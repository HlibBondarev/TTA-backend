using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Tests.Models;

public class CityTests
{
    [Fact]
    public void City_ImplementsIKeyedEntityOfGuid()
    {
        var city = new City();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(city);
    }

    [Fact]
    public void City_ImplementsIKeyedEntity()
    {
        var city = new City();
        Assert.IsAssignableFrom<IKeyedEntity>(city);
    }

    [Fact]
    public void City_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var city = new City { Id = id };
        Assert.Equal(id, city.Id);
    }

    [Fact]
    public void City_RegionId_CanBeSetAndRetrieved()
    {
        var city = new City { RegionId = 42 };
        Assert.Equal(42, city.RegionId);
    }

    [Fact]
    public void City_Name_CanBeSetAndRetrieved()
    {
        var city = new City { Name = "Kyiv" };
        Assert.Equal("Kyiv", city.Name);
    }

    [Fact]
    public void City_DefaultId_IsEmptyGuid()
    {
        var city = new City();
        Assert.Equal(Guid.Empty, city.Id);
    }

    [Fact]
    public void City_DefaultRegionId_IsZero()
    {
        var city = new City();
        Assert.Equal(0, city.RegionId);
    }
}

public class ClubTests
{
    [Fact]
    public void Club_ImplementsIKeyedEntityOfGuid()
    {
        var club = new Club();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(club);
    }

    [Fact]
    public void Club_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var club = new Club { Id = id };
        Assert.Equal(id, club.Id);
    }

    [Fact]
    public void Club_CityId_CanBeSetAndRetrieved()
    {
        var cityId = Guid.NewGuid();
        var club = new Club { CityId = cityId };
        Assert.Equal(cityId, club.CityId);
    }

    [Fact]
    public void Club_Name_CanBeSetAndRetrieved()
    {
        var club = new Club { Name = "FC Dynamo" };
        Assert.Equal("FC Dynamo", club.Name);
    }

    [Fact]
    public void Club_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var club = new Club { CreatedAt = now };
        Assert.Equal(now, club.CreatedAt);
    }
}

public class EventDefinitionTests
{
    [Fact]
    public void EventDefinition_ImplementsIKeyedEntityOfGuid()
    {
        var def = new EventDefinition();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(def);
    }

    [Fact]
    public void EventDefinition_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var def = new EventDefinition { Id = id };
        Assert.Equal(id, def.Id);
    }

    [Fact]
    public void EventDefinition_SportId_CanBeSetAndRetrieved()
    {
        var sportId = Guid.NewGuid();
        var def = new EventDefinition { SportId = sportId };
        Assert.Equal(sportId, def.SportId);
    }

    [Fact]
    public void EventDefinition_Name_CanBeSetAndRetrieved()
    {
        var def = new EventDefinition { Name = "Goal" };
        Assert.Equal("Goal", def.Name);
    }

    [Fact]
    public void EventDefinition_ShortName_CanBeSetAndRetrieved()
    {
        var def = new EventDefinition { ShortName = "G" };
        Assert.Equal("G", def.ShortName);
    }

    [Fact]
    public void EventDefinition_IsPositive_DefaultsToFalse()
    {
        var def = new EventDefinition();
        Assert.False(def.IsPositive);
    }

    [Fact]
    public void EventDefinition_IsPositive_CanBeSetToTrue()
    {
        var def = new EventDefinition { IsPositive = true };
        Assert.True(def.IsPositive);
    }

    [Fact]
    public void EventDefinition_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var def = new EventDefinition { CreatedAt = now };
        Assert.Equal(now, def.CreatedAt);
    }
}

public class GameEventTests
{
    [Fact]
    public void GameEvent_ImplementsIKeyedEntityOfGuid()
    {
        var gameEvent = new GameEvent();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(gameEvent);
    }

    [Fact]
    public void GameEvent_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var gameEvent = new GameEvent { Id = id };
        Assert.Equal(id, gameEvent.Id);
    }

    [Fact]
    public void GameEvent_MatchLineupId_CanBeSetToValue()
    {
        var matchLineupId = Guid.NewGuid();
        var gameEvent = new GameEvent { MatchLineupId = matchLineupId };
        Assert.Equal(matchLineupId, gameEvent.MatchLineupId);
    }

    [Fact]
    public void GameEvent_EventDefinitionId_CanBeSetAndRetrieved()
    {
        var defId = Guid.NewGuid();
        var gameEvent = new GameEvent { EventDefinitionId = defId };
        Assert.Equal(defId, gameEvent.EventDefinitionId);
    }

    [Fact]
    public void GameEvent_PeriodNumber_CanBeSetAndRetrieved()
    {
        var gameEvent = new GameEvent { PeriodNumber = 2 };
        Assert.Equal(2, gameEvent.PeriodNumber);
    }

    [Fact]
    public void GameEvent_NormalizedMatchTime_IsNullable_AndDefaultsToNull()
    {
        var gameEvent = new GameEvent();
        Assert.Null(gameEvent.NormalizedMatchTime);
    }

    [Fact]
    public void GameEvent_NormalizedMatchTime_CanBeSetToTimeSpan()
    {
        var time = TimeSpan.FromMinutes(45);
        var gameEvent = new GameEvent { NormalizedMatchTime = time };
        Assert.Equal(time, gameEvent.NormalizedMatchTime);
    }

    [Fact]
    public void GameEvent_IsLeadToGoal_DefaultsToFalse()
    {
        var gameEvent = new GameEvent();
        Assert.False(gameEvent.IsLeadToGoal);
    }

    [Fact]
    public void GameEvent_IsLeadToGoal_CanBeSetToTrue()
    {
        var gameEvent = new GameEvent { IsLeadToGoal = true };
        Assert.True(gameEvent.IsLeadToGoal);
    }

    [Fact]
    public void GameEvent_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var gameEvent = new GameEvent { CreatedAt = now };
        Assert.Equal(now, gameEvent.CreatedAt);
    }
}

public class MatchTests
{
    [Fact]
    public void Match_ImplementsIKeyedEntityOfGuid()
    {
        var match = new Match();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(match);
    }

    [Fact]
    public void Match_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var match = new Match { Id = id };
        Assert.Equal(id, match.Id);
    }

    [Fact]
    public void Match_TournamentId_CanBeSetAndRetrieved()
    {
        var tournamentId = Guid.NewGuid();
        var match = new Match { TournamentId = tournamentId };
        Assert.Equal(tournamentId, match.TournamentId);
    }

    [Fact]
    public void Match_HomeTeamId_CanBeSetAndRetrieved()
    {
        var teamId = Guid.NewGuid();
        var match = new Match { HomeTeamId = teamId };
        Assert.Equal(teamId, match.HomeTeamId);
    }

    [Fact]
    public void Match_GuestTeamId_CanBeSetAndRetrieved()
    {
        var teamId = Guid.NewGuid();
        var match = new Match { GuestTeamId = teamId };
        Assert.Equal(teamId, match.GuestTeamId);
    }

    [Fact]
    public void Match_MatchNumber_IsNullable_AndDefaultsToNull()
    {
        var match = new Match();
        Assert.Null(match.MatchNumber);
    }

    [Fact]
    public void Match_MatchNumber_CanBeSetToValue()
    {
        var match = new Match { MatchNumber = "M001" };
        Assert.Equal("M001", match.MatchNumber);
    }

    [Fact]
    public void Match_Venue_IsNullable_AndDefaultsToNull()
    {
        var match = new Match();
        Assert.Null(match.Venue);
    }

    [Fact]
    public void Match_Temperature_IsNullable_AndDefaultsToNull()
    {
        var match = new Match();
        Assert.Null(match.Temperature);
    }

    [Fact]
    public void Match_Temperature_CanBeSetToValue()
    {
        var match = new Match { Temperature = 22.5 };
        Assert.Equal(22.5, match.Temperature);
    }

    [Fact]
    public void Match_HomeScore_IsNullable_AndDefaultsToNull()
    {
        var match = new Match();
        Assert.Null(match.HomeScore);
    }

    [Fact]
    public void Match_GuestScore_IsNullable_AndDefaultsToNull()
    {
        var match = new Match();
        Assert.Null(match.GuestScore);
    }

    [Fact]
    public void Match_Scores_CanBeSetToValues()
    {
        var match = new Match { HomeScore = 2, GuestScore = 1 };
        Assert.Equal(2, match.HomeScore);
        Assert.Equal(1, match.GuestScore);
    }

    [Fact]
    public void Match_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var match = new Match { CreatedAt = now };
        Assert.Equal(now, match.CreatedAt);
    }
}

public class MatchLineupTests
{
    [Fact]
    public void MatchLineup_ImplementsIKeyedEntityOfGuid()
    {
        var lineup = new MatchLineup();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(lineup);
    }

    [Fact]
    public void MatchLineup_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var lineup = new MatchLineup { Id = id };
        Assert.Equal(id, lineup.Id);
    }

    [Fact]
    public void MatchLineup_MatchId_CanBeSetAndRetrieved()
    {
        var matchId = Guid.NewGuid();
        var lineup = new MatchLineup { MatchId = matchId };
        Assert.Equal(matchId, lineup.MatchId);
    }

    [Fact]
    public void MatchLineup_PlayerRosterId_CanBeSetAndRetrieved()
    {
        var playerRosterId = Guid.NewGuid();
        var lineup = new MatchLineup { PlayerRosterId = playerRosterId };
        Assert.Equal(playerRosterId, lineup.PlayerRosterId);
    }

    [Fact]
    public void MatchLineup_Number_CanBeSetAndRetrieved()
    {
        var lineup = new MatchLineup { Number = 11 };
        Assert.Equal(11, lineup.Number);
    }

    [Fact]
    public void MatchLineup_PositionId_CanBeSetAndRetrieved()
    {
        var posId = Guid.NewGuid();
        var lineup = new MatchLineup { PositionId = posId };
        Assert.Equal(posId, lineup.PositionId);
    }
}

public class PlayerTests
{
    [Fact]
    public void Player_ImplementsIKeyedEntityOfGuid()
    {
        var player = new Player();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(player);
    }

    [Fact]
    public void Player_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var player = new Player { Id = id };
        Assert.Equal(id, player.Id);
    }

    [Fact]
    public void Player_HomeClubId_CanBeSetAndRetrieved()
    {
        var clubId = Guid.NewGuid();
        var player = new Player { HomeClubId = clubId };
        Assert.Equal(clubId, player.HomeClubId);
    }

    [Fact]
    public void Player_FirstName_CanBeSetAndRetrieved()
    {
        var player = new Player { FirstName = "John" };
        Assert.Equal("John", player.FirstName);
    }

    [Fact]
    public void Player_LastName_CanBeSetAndRetrieved()
    {
        var player = new Player { LastName = "Doe" };
        Assert.Equal("Doe", player.LastName);
    }

    [Fact]
    public void Player_BirthDate_CanBeSetAndRetrieved()
    {
        var birthDate = new DateOnly(1990, 5, 15);
        var player = new Player { BirthDate = birthDate };
        Assert.Equal(birthDate, player.BirthDate);
    }

    [Theory]
    [InlineData(Gender.Male)]
    [InlineData(Gender.Female)]
    public void Player_Gender_CanBeSetAndRetrieved(Gender gender)
    {
        var player = new Player { Gender = gender };
        Assert.Equal(gender, player.Gender);
    }

    [Fact]
    public void Player_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var player = new Player { CreatedAt = now };
        Assert.Equal(now, player.CreatedAt);
    }
}

public class PlayerMetricTests
{
    [Fact]
    public void PlayerMetric_ImplementsIKeyedEntityOfGuid()
    {
        var metric = new PlayerMetric();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(metric);
    }

    [Fact]
    public void PlayerMetric_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var metric = new PlayerMetric { Id = id };
        Assert.Equal(id, metric.Id);
    }

    [Fact]
    public void PlayerMetric_PlayerId_CanBeSetAndRetrieved()
    {
        var playerId = Guid.NewGuid();
        var metric = new PlayerMetric { PlayerId = playerId };
        Assert.Equal(playerId, metric.PlayerId);
    }

    [Fact]
    public void PlayerMetric_Weight_IsNullable_AndDefaultsToNull()
    {
        var metric = new PlayerMetric();
        Assert.Null(metric.Weight);
    }

    [Fact]
    public void PlayerMetric_Weight_CanBeSetToValue()
    {
        var metric = new PlayerMetric { Weight = 75.5 };
        Assert.Equal(75.5, metric.Weight);
    }

    [Fact]
    public void PlayerMetric_Height_IsNullable_AndDefaultsToNull()
    {
        var metric = new PlayerMetric();
        Assert.Null(metric.Height);
    }

    [Fact]
    public void PlayerMetric_Height_CanBeSetToValue()
    {
        var metric = new PlayerMetric { Height = 180.0 };
        Assert.Equal(180.0, metric.Height);
    }

    [Fact]
    public void PlayerMetric_MeasuredAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var metric = new PlayerMetric { MeasuredAt = now };
        Assert.Equal(now, metric.MeasuredAt);
    }
}

public class PlayerPositionDefinitionTests
{
    [Fact]
    public void PlayerPositionDefinition_ImplementsIKeyedEntityOfGuid()
    {
        var def = new PlayerPositionDefinition();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(def);
    }

    [Fact]
    public void PlayerPositionDefinition_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var def = new PlayerPositionDefinition { Id = id };
        Assert.Equal(id, def.Id);
    }

    [Fact]
    public void PlayerPositionDefinition_SportId_CanBeSetAndRetrieved()
    {
        var sportId = Guid.NewGuid();
        var def = new PlayerPositionDefinition { SportId = sportId };
        Assert.Equal(sportId, def.SportId);
    }

    [Fact]
    public void PlayerPositionDefinition_Name_CanBeSetAndRetrieved()
    {
        var def = new PlayerPositionDefinition { Name = "Goalkeeper" };
        Assert.Equal("Goalkeeper", def.Name);
    }

    [Fact]
    public void PlayerPositionDefinition_ShortName_CanBeSetAndRetrieved()
    {
        var def = new PlayerPositionDefinition { ShortName = "GK" };
        Assert.Equal("GK", def.ShortName);
    }
}

public class PlayerPresenceTests
{
    [Fact]
    public void PlayerPresence_ImplementsIKeyedEntityOfGuid()
    {
        var presence = new PlayerPresence();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(presence);
    }

    [Fact]
    public void PlayerPresence_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var presence = new PlayerPresence { Id = id };
        Assert.Equal(id, presence.Id);
    }

    [Fact]
    public void PlayerPresence_MatchLineupId_CanBeSetAndRetrieved()
    {
        var matchLineupId = Guid.NewGuid();
        var presence = new PlayerPresence { MatchLineupId = matchLineupId };
        Assert.Equal(matchLineupId, presence.MatchLineupId);
    }

    [Fact]
    public void PlayerPresence_PeriodNumber_CanBeSetAndRetrieved()
    {
        var presence = new PlayerPresence { PeriodNumber = 1 };
        Assert.Equal(1, presence.PeriodNumber);
    }

    [Fact]
    public void PlayerPresence_TimeIn_CanBeSetAndRetrieved()
    {
        var time = DateTime.UtcNow;
        var presence = new PlayerPresence { TimeIn = time };
        Assert.Equal(time, presence.TimeIn);
    }

    [Fact]
    public void PlayerPresence_TimeOut_IsNullable_AndDefaultsToNull()
    {
        var presence = new PlayerPresence();
        Assert.Null(presence.TimeOut);
    }

    [Fact]
    public void PlayerPresence_TimeOut_CanBeSetToValue()
    {
        var time = DateTime.UtcNow;
        var presence = new PlayerPresence { TimeOut = time };
        Assert.Equal(time, presence.TimeOut);
    }
}

public class PlayerRosterTests
{
    [Fact]
    public void PlayerRoster_ImplementsIKeyedEntityOfGuid()
    {
        var roster = new PlayerRoster();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(roster);
    }

    [Fact]
    public void PlayerRoster_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var roster = new PlayerRoster { Id = id };
        Assert.Equal(id, roster.Id);
    }

    [Fact]
    public void PlayerRoster_PlayerId_CanBeSetAndRetrieved()
    {
        var playerId = Guid.NewGuid();
        var roster = new PlayerRoster { PlayerId = playerId };
        Assert.Equal(playerId, roster.PlayerId);
    }

    [Fact]
    public void PlayerRoster_TournamentId_CanBeSetAndRetrieved()
    {
        var tournamentId = Guid.NewGuid();
        var roster = new PlayerRoster { TournamentId = tournamentId };
        Assert.Equal(tournamentId, roster.TournamentId);
    }

    [Fact]
    public void PlayerRoster_TeamId_CanBeSetAndRetrieved()
    {
        var teamId = Guid.NewGuid();
        var roster = new PlayerRoster { TeamId = teamId };
        Assert.Equal(teamId, roster.TeamId);
    }

    [Fact]
    public void PlayerRoster_Number_CanBeSetAndRetrieved()
    {
        var roster = new PlayerRoster { Number = 7 };
        Assert.Equal(7, roster.Number);
    }

    [Fact]
    public void PlayerRoster_PositionId_CanBeSetAndRetrieved()
    {
        var posId = Guid.NewGuid();
        var roster = new PlayerRoster { PositionId = posId };
        Assert.Equal(posId, roster.PositionId);
    }
}

public class RegionTests
{
    [Fact]
    public void Region_ImplementsIKeyedEntityOfInt()
    {
        var region = new Region();
        Assert.IsAssignableFrom<IKeyedEntity<int>>(region);
    }

    [Fact]
    public void Region_ImplementsIKeyedEntity()
    {
        var region = new Region();
        Assert.IsAssignableFrom<IKeyedEntity>(region);
    }

    [Fact]
    public void Region_Id_CanBeSetAndRetrieved()
    {
        var region = new Region { Id = 5 };
        Assert.Equal(5, region.Id);
    }

    [Fact]
    public void Region_Name_CanBeSetAndRetrieved()
    {
        var region = new Region { Name = "Kyiv Oblast" };
        Assert.Equal("Kyiv Oblast", region.Name);
    }

    [Fact]
    public void Region_DefaultId_IsZero()
    {
        var region = new Region();
        Assert.Equal(0, region.Id);
    }
}

public class SportTests
{
    [Fact]
    public void Sport_ImplementsIKeyedEntityOfGuid()
    {
        var sport = new Sport();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(sport);
    }

    [Fact]
    public void Sport_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var sport = new Sport { Id = id };
        Assert.Equal(id, sport.Id);
    }

    [Fact]
    public void Sport_Name_CanBeSetAndRetrieved()
    {
        var sport = new Sport { Name = "Football" };
        Assert.Equal("Football", sport.Name);
    }

    [Fact]
    public void Sport_DefaultConfigId_IsNullable_AndDefaultsToNull()
    {
        var sport = new Sport();
        Assert.Null(sport.DefaultConfigId);
    }

    [Fact]
    public void Sport_DefaultConfigId_CanBeSetToValue()
    {
        var configId = Guid.NewGuid();
        var sport = new Sport { DefaultConfigId = configId };
        Assert.Equal(configId, sport.DefaultConfigId);
    }
}

public class SportConfigurationTests
{
    [Fact]
    public void SportConfiguration_ImplementsIKeyedEntityOfGuid()
    {
        var config = new SportConfiguration();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(config);
    }

    [Fact]
    public void SportConfiguration_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var config = new SportConfiguration { Id = id };
        Assert.Equal(id, config.Id);
    }

    [Fact]
    public void SportConfiguration_SportId_CanBeSetAndRetrieved()
    {
        var sportId = Guid.NewGuid();
        var config = new SportConfiguration { SportId = sportId };
        Assert.Equal(sportId, config.SportId);
    }

    [Fact]
    public void SportConfiguration_UsesCleanTime_DefaultsToFalse()
    {
        var config = new SportConfiguration();
        Assert.False(config.UsesCleanTime);
    }

    [Fact]
    public void SportConfiguration_UsesCleanTime_CanBeSetToTrue()
    {
        var config = new SportConfiguration { UsesCleanTime = true };
        Assert.True(config.UsesCleanTime);
    }

    [Fact]
    public void SportConfiguration_PeriodsCount_CanBeSetAndRetrieved()
    {
        var config = new SportConfiguration { PeriodsCount = 2 };
        Assert.Equal(2, config.PeriodsCount);
    }

    [Fact]
    public void SportConfiguration_PeriodDurationMinutes_CanBeSetAndRetrieved()
    {
        var config = new SportConfiguration { PeriodDurationMinutes = 45 };
        Assert.Equal(45, config.PeriodDurationMinutes);
    }

    [Fact]
    public void SportConfiguration_FieldSize_IsNullable_AndDefaultsToNull()
    {
        var config = new SportConfiguration();
        Assert.Null(config.FieldSize);
    }

    [Fact]
    public void SportConfiguration_FieldSize_CanBeSetToValue()
    {
        var config = new SportConfiguration { FieldSize = "105x68" };
        Assert.Equal("105x68", config.FieldSize);
    }

    [Fact]
    public void SportConfiguration_RosterLimit_CanBeSetAndRetrieved()
    {
        var config = new SportConfiguration { RosterLimit = 23 };
        Assert.Equal(23, config.RosterLimit);
    }

    [Fact]
    public void SportConfiguration_LineupLimit_CanBeSetAndRetrieved()
    {
        var config = new SportConfiguration { LineupLimit = 11 };
        Assert.Equal(11, config.LineupLimit);
    }

    [Fact]
    public void SportConfiguration_ActivePlayersLimit_CanBeSetAndRetrieved()
    {
        var config = new SportConfiguration { ActivePlayersLimit = 7 };
        Assert.Equal(7, config.ActivePlayersLimit);
    }
}

public class TeamTests
{
    [Fact]
    public void Team_ImplementsIKeyedEntityOfGuid()
    {
        var team = new Team();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(team);
    }

    [Fact]
    public void Team_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var team = new Team { Id = id };
        Assert.Equal(id, team.Id);
    }

    [Fact]
    public void Team_ClubId_CanBeSetAndRetrieved()
    {
        var clubId = Guid.NewGuid();
        var team = new Team { ClubId = clubId };
        Assert.Equal(clubId, team.ClubId);
    }

    [Fact]
    public void Team_Name_CanBeSetAndRetrieved()
    {
        var team = new Team { Name = "U-21 Team" };
        Assert.Equal("U-21 Team", team.Name);
    }

    [Fact]
    public void Team_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var team = new Team { CreatedAt = now };
        Assert.Equal(now, team.CreatedAt);
    }
}

public class TeamMembershipTests
{
    [Fact]
    public void TeamMembership_ImplementsIKeyedEntityOfGuid()
    {
        var membership = new TeamMembership();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(membership);
    }

    [Fact]
    public void TeamMembership_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var membership = new TeamMembership { Id = id };
        Assert.Equal(id, membership.Id);
    }

    [Fact]
    public void TeamMembership_UserId_CanBeSetAndRetrieved()
    {
        var membership = new TeamMembership { UserId = "auth0|abc123" };
        Assert.Equal("auth0|abc123", membership.UserId);
    }

    [Fact]
    public void TeamMembership_TeamId_CanBeSetAndRetrieved()
    {
        var teamId = Guid.NewGuid();
        var membership = new TeamMembership { TeamId = teamId };
        Assert.Equal(teamId, membership.TeamId);
    }

    [Theory]
    [InlineData(TeamRole.HeadCoach)]
    [InlineData(TeamRole.Player)]
    [InlineData(TeamRole.Captain)]
    public void TeamMembership_RoleInTeam_CanBeSetAndRetrieved(TeamRole role)
    {
        var membership = new TeamMembership { RoleInTeam = role };
        Assert.Equal(role, membership.RoleInTeam);
    }

    [Fact]
    public void TeamMembership_JoinedAt_CanBeSetAndRetrieved()
    {
        var date = new DateTime(2023, 1, 15);
        var membership = new TeamMembership { JoinedAt = date };
        Assert.Equal(date, membership.JoinedAt);
    }

    [Fact]
    public void TeamMembership_LeftAt_IsNullable_AndDefaultsToNull()
    {
        var membership = new TeamMembership();
        Assert.Null(membership.LeftAt);
    }

    [Fact]
    public void TeamMembership_LeftAt_CanBeSetToValue()
    {
        var date = new DateTime(2024, 6, 1);
        var membership = new TeamMembership { LeftAt = date };
        Assert.Equal(date, membership.LeftAt);
    }

    [Fact]
    public void TeamMembership_IsPrimary_DefaultsToFalse()
    {
        var membership = new TeamMembership();
        Assert.False(membership.IsPrimary);
    }

    [Fact]
    public void TeamMembership_IsPrimary_CanBeSetToTrue()
    {
        var membership = new TeamMembership { IsPrimary = true };
        Assert.True(membership.IsPrimary);
    }
}

public class TimeAnchorTests
{
    [Fact]
    public void TimeAnchor_ImplementsIKeyedEntityOfGuid()
    {
        var anchor = new TimeAnchor();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(anchor);
    }

    [Fact]
    public void TimeAnchor_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var anchor = new TimeAnchor { Id = id };
        Assert.Equal(id, anchor.Id);
    }

    [Fact]
    public void TimeAnchor_MatchId_CanBeSetAndRetrieved()
    {
        var matchId = Guid.NewGuid();
        var anchor = new TimeAnchor { MatchId = matchId };
        Assert.Equal(matchId, anchor.MatchId);
    }

    [Fact]
    public void TimeAnchor_PeriodNumber_CanBeSetAndRetrieved()
    {
        var anchor = new TimeAnchor { PeriodNumber = 1 };
        Assert.Equal(1, anchor.PeriodNumber);
    }

    [Theory]
    [InlineData(TimeAnchorType.PeriodStart)]
    [InlineData(TimeAnchorType.PeriodEnd)]
    [InlineData(TimeAnchorType.StoppageStart)]
    [InlineData(TimeAnchorType.StoppageEnd)]
    public void TimeAnchor_Type_CanBeSetAndRetrieved(TimeAnchorType type)
    {
        var anchor = new TimeAnchor { Type = type };
        Assert.Equal(type, anchor.Type);
    }

    [Fact]
    public void TimeAnchor_Timestamp_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var anchor = new TimeAnchor { Timestamp = now };
        Assert.Equal(now, anchor.Timestamp);
    }
}

public class TournamentTests
{
    [Fact]
    public void Tournament_ImplementsIKeyedEntityOfGuid()
    {
        var tournament = new Tournament();
        Assert.IsAssignableFrom<IKeyedEntity<Guid>>(tournament);
    }

    [Fact]
    public void Tournament_Id_CanBeSetAndRetrieved()
    {
        var id = Guid.NewGuid();
        var tournament = new Tournament { Id = id };
        Assert.Equal(id, tournament.Id);
    }

    [Fact]
    public void Tournament_SportId_CanBeSetAndRetrieved()
    {
        var sportId = Guid.NewGuid();
        var tournament = new Tournament { SportId = sportId };
        Assert.Equal(sportId, tournament.SportId);
    }

    [Fact]
    public void Tournament_ConfigurationId_CanBeSetAndRetrieved()
    {
        var configId = Guid.NewGuid();
        var tournament = new Tournament { ConfigurationId = configId };
        Assert.Equal(configId, tournament.ConfigurationId);
    }

    [Fact]
    public void Tournament_Name_CanBeSetAndRetrieved()
    {
        var tournament = new Tournament { Name = "Premier League" };
        Assert.Equal("Premier League", tournament.Name);
    }

    [Fact]
    public void Tournament_StartDate_CanBeSetAndRetrieved()
    {
        var date = new DateTime(2024, 8, 1);
        var tournament = new Tournament { StartDate = date };
        Assert.Equal(date, tournament.StartDate);
    }

    [Fact]
    public void Tournament_EndDate_IsNullable_AndDefaultsToNull()
    {
        var tournament = new Tournament();
        Assert.Null(tournament.EndDate);
    }

    [Fact]
    public void Tournament_EndDate_CanBeSetToValue()
    {
        var date = new DateTime(2025, 5, 31);
        var tournament = new Tournament { EndDate = date };
        Assert.Equal(date, tournament.EndDate);
    }

    [Fact]
    public void Tournament_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var tournament = new Tournament { CreatedAt = now };
        Assert.Equal(now, tournament.CreatedAt);
    }
}

public class UserTests
{
    [Fact]
    public void User_ImplementsIKeyedEntityOfString()
    {
        var user = new User();
        Assert.IsAssignableFrom<IKeyedEntity<string>>(user);
    }

    [Fact]
    public void User_ImplementsIKeyedEntity()
    {
        var user = new User();
        Assert.IsAssignableFrom<IKeyedEntity>(user);
    }

    [Fact]
    public void User_Id_CanBeSetAndRetrieved()
    {
        var user = new User { Id = "auth0|user123" };
        Assert.Equal("auth0|user123", user.Id);
    }

    [Fact]
    public void User_Email_CanBeSetAndRetrieved()
    {
        var user = new User { Email = "user@example.com" };
        Assert.Equal("user@example.com", user.Email);
    }

    [Fact]
    public void User_DisplayName_CanBeSetAndRetrieved()
    {
        var user = new User { DisplayName = "John Doe" };
        Assert.Equal("John Doe", user.DisplayName);
    }

    [Fact]
    public void User_CreatedAt_CanBeSetAndRetrieved()
    {
        var now = DateTime.UtcNow;
        var user = new User { CreatedAt = now };
        Assert.Equal(now, user.CreatedAt);
    }
}