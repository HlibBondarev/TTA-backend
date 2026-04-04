using MediatR;
using TTA.Common.Extensions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;

namespace TTA.BusinessLogic.Features.Teams.Commands;

/// <summary>
/// Command to create a new team, associated with a specific club.
/// </summary>
public record CreateTeamCommand(
    Guid ClubId,
    string Name,
    Guid SportId,
    int? MinBirthYear,
    Gender Gender) : IRequest<Guid>;

public static class CreateTeamCommandExtensions
{
    /// <summary>
    /// Maps the command to a <see cref="Team"/> domain model.
    /// </summary>
    public static Team ToModel(this CreateTeamCommand cmd) => new()
    {
        Id = Guid.NewGuid(),
        ClubId = cmd.ClubId,
        SportId = cmd.SportId,
        Name = cmd.Name,
        MinBirthYear = cmd.MinBirthYear,
        Gender = cmd.Gender,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Maps a collection of CreateTeamCommand to a list of Team entities.
    /// </summary>
    /// <param name="list">Collection of commands.</param>
    /// <returns>A list of Team entities.</returns>
    public static List<Team> ToModel(this IEnumerable<CreateTeamCommand> list)
        => list.MapToList(ToModel);
}