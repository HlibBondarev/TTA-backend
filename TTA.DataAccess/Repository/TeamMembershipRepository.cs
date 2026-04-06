using Dapper;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for managing TeamMembership entities in PostgreSQL using Dapper.
/// Inherits from <see cref="EntityRepositoryBase{Guid, TeamMembership}"/> for core transaction and connection logic.
/// </summary>
/// <param name="connectionFactory">The factory to create database connections.</param>
public class TeamMembershipRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, TeamMembership>(connectionFactory), ITeamMembershipRepository
{
    /// <inheritdoc />
    public async Task<TeamMembership> CreateMembershipAsync(TeamMembership membership, CancellationToken ct = default)
    {
        // Dapper automatically maps TeamMembership properties to @parameters
        return await CreateOrUpdate(
            membership,
            SqlStatements.ForTeamMemberships.UpsertMembership,
            new DynamicParameters(membership),
            ct);
    }

    /// <inheritdoc />
    public async Task<bool> TerminateMembershipAsync(Guid membershipId, CancellationToken ct = default)
    {
        // Using the base Delete method to perform a Soft Delete (UPDATE) within a transaction.
        // It returns true if affectedRows > 0, indicating a successful termination.
        return await Delete(
            membershipId,
            SqlStatements.ForTeamMemberships.TerminateMembership,
            ct);
    }

    /// <inheritdoc />
    public async Task<string?> GetMembersJsonAsync(Guid teamId, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_team_id", teamId);

        return await GetDataInJson(
            SqlStatements.ForTeamMemberships.GetMembersJson,
            parameters,
            ct);
    }
}