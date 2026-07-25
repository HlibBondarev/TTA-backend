namespace TTA.BusinessLogic.Features.EventDefinitions.DTOs;

/// <summary>
/// Data transfer object representing a game event definition in the context of a match.
/// </summary>
/// <param name="Id">The unique identifier of the event definition.</param>
/// <param name="SportId">The unique identifier of the associated sport.</param>
/// <param name="Name">The display name of the event type (e.g., Goal, Exclusion).</param>
/// <param name="ShortName">The abbreviated name/code of the event type.</param>
/// <param name="IsPositive">Indicates whether the event action has a positive statistical impact.</param>
/// <param name="CreatedAt">The UTC date and time when the event definition was created.</param>
public record EventDefinitionForMatchResponse(
    Guid Id,
    Guid SportId,
    string Name,
    string ShortName,
    bool IsPositive,
    DateTime CreatedAt);