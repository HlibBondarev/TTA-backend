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
    public async Task<bool> HasExistingClubOwnershipAsync(string userId, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        return await ExecuteQueryInTransaction<bool>(
            SqlStatements.ForClubs.CheckUserOwnsAnyClub,
            parameters,
            cancellationToken: cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<bool> HasClubOwnershipAsync(string userId, Guid clubId, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("TargetType", TargetScope.Club);
        parameters.Add("TargetId", clubId);

        // result is now <int?> because the SQL function returns INT (0, 1, or 2)
        var result = await ExecuteQueryInTransaction<int?>(
            SqlStatements.ForAccessPolicies.GetUserPermission,
            parameters,
            cancellationToken: cancellationToken
        );

        // FullControl (0) means the user owns the club.
        // Compare with the enum value directly. 
        return result.HasValue && (AppRole)result.Value == AppRole.FullControl;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateWithOwnershipAsync(Club club, string userId, string userEmail, string userName, CancellationToken cancellationToken)
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
        // Internal SQL logic now uses INT constants (targettype = 1, role = 0)
        return await ExecuteQueryInTransaction<Guid>(
            SqlStatements.ForClubs.CreateClubWithOwnership,
            parameters,
            cancellationToken: cancellationToken
        );
    }
}