using MediatR;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;

namespace TTA.BusinessLogic.Features.EventDefinitions.Commands;

/// <summary>
/// Command to save user active event definition presets and display layout order for a sport.
/// </summary>
/// <param name="UserId">The unique identifier of the target user.</param>
/// <param name="SportId">The unique identifier of the target sport.</param>
/// <param name="EventDefinitionIds">The ordered collection of enabled event definition identifiers.</param>
public record SaveUserEventPresetCommand(
    string UserId,
    Guid SportId,
    IEnumerable<Guid> EventDefinitionIds) : IRequest;

/// <summary>
/// Extension methods for mapping SaveUserEventPresetRequest DTO to SaveUserEventPresetCommand.
/// </summary >
public static class SaveUserEventPresetCommandExtensions
{
    /// <summary>
    /// Maps SaveUserEventPresetRequest DTO to SaveUserEventPresetCommand.
    /// </summary>
    /// <param name="request" >The request DTO.</param >
    /// <param name="userId" >The user ID.</param>
    /// <param name="sportId" >The sport ID.</param >
    /// <returns>The mapped command.</returns>
    public static SaveUserEventPresetCommand ToCommand(
        this SaveUserEventPresetRequest request,
        string userId,
        Guid sportId)
    {
        return new SaveUserEventPresetCommand(
            UserId: userId,
            SportId: sportId,
            EventDefinitionIds: request.EventDefinitionIds ?? []);
    }
}