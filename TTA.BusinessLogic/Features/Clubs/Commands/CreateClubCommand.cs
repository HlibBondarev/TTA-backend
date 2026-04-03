using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Clubs.Commands;

/// <summary>
/// Command to create a club, including the creator's full identity 
/// to support Just-in-Time user registration in the local database.
/// </summary>
/// <param name="Name">The name of the club to be created.</param>
/// <param name="CityId">The unique identifier of the city where the club is located.</param>
/// <param name="CreatorUserId">The unique identifier of the user from the identity provider (e.g., Auth0).</param>
/// <param name="CreatorEmail">The email address of the creator, used for JIT user creation.</param>
/// <param name="CreatorDisplayName">The display name or nickname of the creator, used for JIT user creation.</param>
public record CreateClubCommand(
    string Name,
    Guid CityId,
    string CreatorUserId,
    string? CreatorEmail,
    string CreatorDisplayName) : IRequest<Guid>;

/// <summary>
/// Provides extension methods for mapping <see cref="CreateClubCommand"/> to domain entities.
/// </summary>
public static class CreateClubCommandExtensions
{
    /// <summary>
    /// Maps a <see cref="CreateClubCommand"/> to a new <see cref="Club"/> domain model.
    /// Note: This mapping focuses on the Club entity itself; ownership logic is handled by the repository/database.
    /// </summary>
    /// <param name="cmd">The command instance containing club and creator data.</param>
    /// <returns>A new <see cref="Club"/> entity initialized with a fresh ID and current UTC timestamp.</returns>
    public static Club ToModel(this CreateClubCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        Name = cmd.Name,
        CityId = cmd.CityId,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a collection of <see cref="CreateClubCommand"/> to a list of <see cref="Club"/> domain models.
    /// </summary>
    /// <param name="list">The collection of commands to map.</param>
    /// <returns>A list of initialized <see cref="Club"/> entities.</returns>
    public static List<Club> ToModel(this IEnumerable<CreateClubCommand> list)
        => list.MapToList(ToModel);
}