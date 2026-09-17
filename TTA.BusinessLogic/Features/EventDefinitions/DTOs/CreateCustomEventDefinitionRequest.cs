namespace TTA.BusinessLogic.Features.EventDefinitions.DTOs;

/// <summary >
/// Request data transfer object for creating a custom user-owned event definition.
/// </summary>
/// <param name="Id">The client-generated unique identifier of the custom event definition.</param>
/// <param name="Name">The display name of the custom event definition.</param>
/// <param name="ShortName">The abbreviated short code of the custom event definition.</param>
/// <param name="IsPositive">Indicates whether the custom event action has a positive statistical impact.</param>
public record CreateCustomEventDefinitionRequest(
    Guid Id,
    string Name,
    string ShortName,
    bool IsPositive);