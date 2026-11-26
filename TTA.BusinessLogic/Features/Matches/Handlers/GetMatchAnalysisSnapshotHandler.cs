using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Features.Matches.Handlers;

/// <summary>
/// Handles retrieval of the match analysis snapshot and maps database projections to response DTOs.
/// </summary>
/// <param name="repository">The match repository for data access.</param>
/// <param name="logger">The logger instance for diagnostic messages.</param>
public class GetMatchAnalysisSnapshotHandler(
    IMatchRepository repository,
    ILogger<GetMatchAnalysisSnapshotHandler> logger) : IRequestHandler<GetMatchAnalysisSnapshotQuery, MatchAnalysisSnapshotResponse>
{
    /// <summary>
    /// Fetches the snapshot projection and maps it to <see cref="MatchAnalysisSnapshotResponse"/>.
    /// </summary>
    /// <param name="request">The query containing the match and tracked team identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The match analysis snapshot; the lineup is empty when the tracked team has no lineup entries.</returns>
    /// <exception cref="NotFoundException">Thrown if the match does not exist or the team does not participate in it.</exception>
    public async Task<MatchAnalysisSnapshotResponse> Handle(GetMatchAnalysisSnapshotQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retrieving analysis snapshot for match {MatchId} and team {TeamId}.", request.MatchId, request.TeamId);

        var snapshot = await repository.GetMatchAnalysisSnapshotAsync(request.MatchId, request.TeamId, cancellationToken);

        if (snapshot is null)
        {
            logger.LogWarning("Match {MatchId} was not found or team {TeamId} does not participate in it.", request.MatchId, request.TeamId);
            throw new NotFoundException($"Match with ID {request.MatchId} was not found or team {request.TeamId} does not participate in it.");
        }

        return MapSnapshot(snapshot, request.TeamId);
    }

    private static MatchAnalysisSnapshotResponse MapSnapshot(MatchAnalysisSnapshotProjection snapshot, Guid trackedTeamId)
    {
        var context = snapshot.Context;

        return new MatchAnalysisSnapshotResponse(
            MapMatch(context),
            MapTeams(context, trackedTeamId),
            new MatchAnalysisSportResponse(context.SportId, context.SportName, context.SportShortName),
            MapConfiguration(context),
            snapshot.Lineup.Select(MapLineupPlayer).ToList(),
            snapshot.EventDefinitions.Select(MapEventDefinition).ToList());
    }

    private static MatchAnalysisMatchResponse MapMatch(MatchAnalysisContextProjection context) => new(
        context.MatchId,
        context.TournamentId,
        context.TournamentName,
        context.ScheduledAt,
        context.MatchNumber,
        context.Venue,
        context.Temperature,
        context.HomeScore,
        context.GuestScore);

    private static MatchAnalysisTeamsResponse MapTeams(MatchAnalysisContextProjection context, Guid trackedTeamId)
    {
        var home = new MatchAnalysisTeamResponse(context.HomeTeamId, context.HomeTeamName);
        var guest = new MatchAnalysisTeamResponse(context.GuestTeamId, context.GuestTeamName);
        var isTrackedHome = context.HomeTeamId == trackedTeamId;

        return new MatchAnalysisTeamsResponse(
            home,
            guest,
            Tracked: isTrackedHome ? home : guest,
            Opponent: isTrackedHome ? guest : home);
    }

    private static MatchAnalysisSportConfigurationResponse MapConfiguration(MatchAnalysisContextProjection context) => new(
        context.ConfigurationId,
        context.UsesCleanTime,
        context.PeriodsCount,
        context.PeriodDurationMinutes,
        context.FieldSize,
        context.RosterLimit,
        context.LineupLimit,
        context.ActivePlayersLimit,
        context.FieldLength,
        context.FieldWidth,
        context.MaxPersonalFouls,
        context.Playground,
        context.ShotMap);

    private static MatchAnalysisLineupPlayerResponse MapLineupPlayer(MatchAnalysisLineupProjection player) => new(
        player.Id,
        player.PlayerRosterId,
        player.FirstName,
        player.LastName,
        player.Number,
        player.PositionId,
        player.PositionName,
        player.PositionShortName);

    private static MatchAnalysisEventDefinitionResponse MapEventDefinition(MatchAnalysisEventDefinitionProjection definition) => new(
        definition.Id,
        definition.Name,
        definition.ShortName,
        definition.IsPositive,
        definition.IsShot,
        definition.Score,
        definition.FoulImpact,
        definition.PenaltyDurationSeconds);
}
