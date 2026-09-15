namespace TTA.BusinessLogic.Features.EventDefinitions.DTOs;

/// <summary>
/// Data transfer object representing an event definition enriched with preset state and layout sort order.
/// </summary>
/// <param name="Id">The unique identifier of the event definition.</param>
/// <param name="SportId">The unique identifier of the associated sport.</param>
/// <param name="Name">The display name of the event type.</param>
/// <param name="ShortName">The abbreviated short code of the event type.</param>
/// <param name="IsPositive">Indicates whether the event action has a positive statistical impact.</param>
/// <param name="IsCustom">Indicates whether this definition is custom user-owned.</param>
/// <param name="IsEnabled">Indicates whether this definition is enabled in the user preset layout.</param>
/// <param name="SortOrder">The display layout sort order index.</param>
public record EventDefinitionResponse(
    Guid Id,
    Guid SportId,
    string Name,
    string ShortName,
    bool IsPositive,
    bool IsCustom,
    bool IsEnabled,
    int SortOrder);