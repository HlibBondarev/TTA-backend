using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Clubs.Commands;

/// <summary>
/// Command to create a club, including the creator's identity.
/// </summary>
/// <param name="Name">Club name.</param>
/// <param name="CityId">City ID.</param>
/// <param name="CreatorUserId">The ID of the user who will own the club (from Auth0).</param>
public record CreateClubCommand(
    string Name,
    Guid CityId,
    string CreatorUserId) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping CreateClubCommand to domain models.
/// </summary>
public static class CreateClubCommandExtensions
{
    /// <summary>
    /// Maps a single CreateClubCommand to a Club entity.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <returns>A new Club entity initialized with command data.</returns>
    public static Club ToModel(this CreateClubCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        Name = cmd.Name,
        CityId = cmd.CityId,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a collection of CreateClubCommand to a list of Club entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <returns>A list of Club entities.</returns>
    public static List<Club> ToModel(this IEnumerable<CreateClubCommand> list)
        => list.MapToList(ToModel);
}