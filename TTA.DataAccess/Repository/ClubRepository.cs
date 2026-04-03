using Dapper;
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

        // FIX: Change <string> to <int?> because auth.get_user_permission now RETURNS INT
        var result = await ExecuteQueryInTransaction<int?>(
            SqlStatements.ForAccessPolicies.GetUserPermission,
            parameters,
            ct: ct
        );

        // FIX: Compare with the enum value directly. 
        // FullControl (0) means the user owns the club.
        return result.HasValue && (AppRole)result.Value == AppRole.FullControl;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateWithOwnershipAsync(Club club, string userId, string userEmail, string userName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(club);

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(userEmail)) throw new ArgumentException("Email cannot be empty", nameof(userEmail));
        if (string.IsNullOrWhiteSpace(userName) || userName.Length < 3)
            throw new ArgumentException("User name must be at least 3 characters long", nameof(userName));

        var parameters = new DynamicParameters();
        parameters.Add("Id", club.Id);
        parameters.Add("CityId", club.CityId);
        parameters.Add("Name", club.Name);
        parameters.Add("OwnerId", userId);
        parameters.Add("OwnerEmail", userEmail);
        parameters.Add("OwnerName", userName);
        parameters.Add("CreatedAt", club.CreatedAt);

        // Executes the atomic creation function (Clubs + AccessPolicies)
        return await ExecuteQueryInTransaction<Guid>(
            SqlStatements.ForClubs.CreateClubWithOwnership,
            parameters,
            ct: ct
        );
    }
}