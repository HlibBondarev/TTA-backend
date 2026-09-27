namespace TTA.DataAccess.Repository.Projections;

/// <summary>
/// Data transfer object representing an event definition enriched with user preset state and display order.
/// </summary>
/// <param name="Id">The unique identifier of the event definition.</param>
/// <param name="SportId">The unique identifier of the associated sport.</param>
/// <param name="Name">The display name of the event definition.</param>
/// <param name="ShortName">The abbreviated short name of the event definition.</param>
/// <param name="IsPositive">Indicates whether the event action has a positive statistical impact.</param>
/// <param name="IsCustom">Indicates whether this definition is custom user-owned.</param>
/// <param name="IsEnabled">Indicates whether this definition is actively enabled in the user preset.</param>
/// <param name="SortOrder">The display layout sort order index.</param>
public record UserEventDefinitionProjection(
    Guid Id,
    Guid SportId,
    string Name,
    string ShortName,
    bool IsPositive,
    bool IsCustom,
    bool IsEnabled,
    int SortOrder);
