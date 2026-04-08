using Dapper;
using TTA.Common.Enums;
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
    public async Task<TeamMembership> CreateMembershipWithPolicyAsync(TeamMembership membership, AppRole appRole, CancellationToken ct = default)
    {
        // We only add AppRole; other params are mapped from the membership entity automatically by BaseRepository
        var parameters = new DynamicParameters();
        parameters.Add("AppRole", (int)appRole);

        return await CreateOrUpdate(
            membership,
            SqlStatements.ForTeamMemberships.UpsertMembershipWithPolicy,
            parameters,
            ct);
    }

    /// <inheritdoc />
    public async Task<bool> TerminateMembershipAsync(Guid teamId, Guid membershipId, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("p_team_id", teamId);
        parameters.Add("p_membership_id", membershipId);

        // Using ExecuteQueryInTransaction to get the boolean result from the function
        return await ExecuteQueryInTransaction<bool>(
            SqlStatements.ForTeamMemberships.TerminateMembership,
            parameters,
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