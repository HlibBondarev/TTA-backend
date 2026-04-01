using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Players.Commands;

/// <summary>
/// Command to create a new player (athlete) within a specific club.
/// </summary>
/// <param name="HomeClubId">The unique identifier of the club the player belongs to.</param>
/// <param name="FirstName">Player's first name.</param>
/// <param name="LastName">Player's last name.</param>
/// <param name="BirthDate">Player's date of birth.</param>
/// <param name="Gender">Player's gender (Male/Female).</param>
public record CreatePlayerCommand(
    Guid HomeClubId,
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    Gender Gender) : IRequest<Guid>;

/// <summary>
/// Extensions for mapping CreatePlayerCommand to domain models.
/// </summary>
public static class CreatePlayerCommandExtensions
{
    /// <summary>
    /// Maps a single CreatePlayerCommand to a Player entity.
    /// </summary>
    /// <param name="cmd">The command instance.</param>
    /// <returns>A new Player entity initialized with command data.</returns>
    public static Player ToModel(this CreatePlayerCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        HomeClubId = cmd.HomeClubId,
        FirstName = cmd.FirstName,
        LastName = cmd.LastName,
        BirthDate = cmd.BirthDate,
        Gender = cmd.Gender,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a collection of CreatePlayerCommand to a list of Player entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <returns>A list of Player entities.</returns>
    public static List<Player> ToModel(this IEnumerable<CreatePlayerCommand> list)
        => list.MapToList(ToModel);
}