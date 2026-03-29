using Dapper;
using System.Data;
using TTA.Common.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;
using TTA.DataAccess.Repository.Base;

namespace TTA.DataAccess.Repository;

/// <summary>
/// Repository for Club operations using Dapper and explicit SQL function calls.
/// Aligned with AccessPolicies schema (using TargetId).
/// </summary>
public class ClubRepository(IDbConnectionFactory connectionFactory)
    : EntityRepositoryBase<Guid, Club>(connectionFactory), IClubRepository
{
    /// <inheritdoc />
    public async Task<bool> HasExistingClubOwnershipAsync(string userId, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        return await ExecuteQueryInTransaction<bool>(
            SqlStatements.ForClubs.CheckUserOwnsAnyClub,
            parameters,
            commandType: CommandType.Text,
            ct: ct
        );
    }

    /// <inheritdoc />
    public async Task<bool> HasClubOwnershipAsync(string userId, Guid clubId, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("TargetType", TargetScope.Club.ToString());
        parameters.Add("TargetId", clubId);

        var result = await ExecuteQueryInTransaction<string>(
            SqlStatements.ForAccessPolicies.GetUserPermission,
            parameters,
            commandType: CommandType.Text,
            ct: ct
        );

        return result == AppRole.FullControl.ToString();
    }

    /// <inheritdoc />
    public async Task<Guid> CreateWithOwnershipAsync(Club club, string userId, CancellationToken ct)
    {
        if (club == null)
            throw new ArgumentNullException(nameof(club));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

        var parameters = new DynamicParameters();
        parameters.Add("Id", club.Id);
        parameters.Add("CityId", club.CityId);
        parameters.Add("Name", club.Name);
        parameters.Add("OwnerId", userId);
        parameters.Add("CreatedAt", club.CreatedAt);

        // Executes the atomic creation function (Clubs + AccessPolicies)
        return await ExecuteQueryInTransaction<Guid>(
            SqlStatements.ForClubs.CreateClubWithOwnership,
            parameters,
            commandType: CommandType.Text,
            ct: ct
        );
    }
}