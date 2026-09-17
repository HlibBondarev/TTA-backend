namespace TTA.BusinessLogic.Features.EventDefinitions.DTOs;

/// <summary>
/// Request data transfer object for saving active user event preset selections and display layout order.
/// </summary>
/// <param name="EventDefinitionIds">The ordered collection of enabled event definition identifiers.</param>
public record SaveUserEventPresetRequest(IEnumerable<Guid> EventDefinitionIds);