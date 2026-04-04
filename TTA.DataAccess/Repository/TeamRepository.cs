using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for managing Team entities in PostgreSQL using Dapper.
/// Inherits from <see cref="EntityRepositoryBase{Guid, Team}"/> for common CRUD logic.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class TeamRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, Team>(connectionFactory), ITeamRepository
{
    /// <summary>
    /// Creates a new team or updates an existing one using a stored function.
    /// Mapping of Gender (Enum) to the database-expected format is handled here.
    /// </summary>
    /// <param name="team">The team entity to persist.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The persisted <see cref="Team"/> entity.</returns>
    public async Task<Team> CreateTeamAsync(Team team, CancellationToken ct)
    {
        // Use the entity itself as the base for parameters
        var parameters = new DynamicParameters(team);

        // Explicitly map Gender enum to int for the PostgreSQL function parameter
        parameters.Add("Gender", (int)team.Gender);

        return await CreateOrUpdate(
            team,
            SqlStatements.ForTeams.UpsertTeam,
            parameters,
            ct);
    }

    /// <summary>
    /// Retrieves all teams associated with a specific club using a stored function.
    /// </summary>
    /// <param name="clubId">The unique identifier of the club.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A collection of <see cref="Team"/> entities.</returns>
    public async Task<IEnumerable<Team>> GetByClubIdAsync(Guid clubId, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_club_id", clubId);

        // Aligned with PlayerRepository: use GetByPropValues for collections
        return await GetByPropValues(
            SqlStatements.ForTeams.GetTeamsByClub,
            parameters,
            ct);
    }
}