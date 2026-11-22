using TTA.DataAccess.Enums;
using TTA.DataAccess.Repository.Projections;

namespace TTA.BusinessLogic.Features.EventDefinitions.DTOs;

/// <summary>
/// Data transfer object representing an event definition enriched with preset state and layout sort order.
/// </summary>
/// <param name="Id">The unique identifier of the event definition.</param>
/// <param name="SportId">The unique identifier of the associated sport.</param>
/// <param name="Name">The display name of the event type.</param>
/// <param name="ShortName">The abbreviated short code of the event type.</param>
/// <param name="IsPositive">Indicates whether the event action has a positive statistical impact.</param>
/// <param name="IsShot">Indicates whether the event action requires goal shot target location tracking.</param>
/// <param name="Score">The score points awarded for this event action.</param>
/// <param name="FoulImpact">The disciplinary impact of the event on the player.</param>
/// <param name="PenaltyDurationSeconds">The temporary team shorthanded penalty duration in clean match seconds, or null when no timed team penalty applies (or when combined with <see cref="FoulImpact.DisqualificationNoSub"/>, indicating the team remains shorthanded until match end).</param>
/// <param name="IsCustom">Indicates whether this definition is custom user-owned.</param>
/// <param name="IsEnabled">Indicates whether this definition is enabled in the user preset layout.</param>
/// <param name="SortOrder">The display layout sort order index.</param>
public record EventDefinitionResponse(
    Guid Id,
    Guid SportId,
    string Name,
    string ShortName,
    bool IsPositive,
    bool IsShot,
    int Score,
    FoulImpact FoulImpact,
    int? PenaltyDurationSeconds,
    bool IsCustom,
    bool IsEnabled,
    int SortOrder);

/// <summary>
/// Extension methods for mapping <see cref="UserEventDefinitionProjection"/> to <see cref="EventDefinitionResponse"/>.
/// </summary>
public static class EventDefinitionResponseExtensions
{
    /// <summary>
    /// Maps a <see cref="UserEventDefinitionProjection"/> to an <see cref="EventDefinitionResponse"/> DTO.
    /// </summary>
    /// <param name="projection">The user event definition projection.</param>
    /// <returns>The mapped response DTO.</returns>
    public static EventDefinitionResponse ToResponse(this UserEventDefinitionProjection projection)
    {
        return new EventDefinitionResponse(
            Id: projection.Id,
            SportId: projection.SportId,
            Name: projection.Name,
            ShortName: projection.ShortName,
            IsPositive: projection.IsPositive,
            IsShot: projection.IsShot,
            Score: projection.Score,
            FoulImpact: projection.FoulImpact,
            PenaltyDurationSeconds: projection.PenaltyDurationSeconds,
            IsCustom: projection.IsCustom,
            IsEnabled: projection.IsEnabled,
            SortOrder: projection.SortOrder);
    }
}