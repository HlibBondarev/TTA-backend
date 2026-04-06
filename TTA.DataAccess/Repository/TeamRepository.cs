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
    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<Team?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await GetById(id, SqlStatements.ForTeams.GetTeamById, ct);
    }
}