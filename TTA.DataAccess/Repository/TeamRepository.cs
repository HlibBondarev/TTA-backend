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
    /// Asynchronously creates or updates a team record in the database.
    /// </summary>
    /// <param name="team">The team entity to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted <see cref="Team"/> entity.</returns>
    /// <remarks>
    /// Calls the <see cref="SqlStatements.ForTeams.UpsertTeam"/> stored function.
    /// Relies on Dapper to automatically map <see cref="Team"/> properties (including enums) 
    /// to the function parameters as integers.
    /// </remarks>
    public async Task<Team> CreateTeamAsync(Team team, CancellationToken ct = default)
    {
        // No manual parameter mapping needed. 
        // Dapper maps all properties of the 'team' object (including Enum as int)
        // to the @parameters in SqlStatements.ForTeams.UpsertTeam.
        return await CreateOrUpdate(
            team,
            SqlStatements.ForTeams.UpsertTeam,
            new DynamicParameters(team),
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