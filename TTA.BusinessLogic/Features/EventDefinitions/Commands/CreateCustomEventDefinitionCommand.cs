using MediatR;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Features.EventDefinitions.Commands;

/// <summary>
/// Command to create a custom user-owned event definition.
/// </summary>
/// <param name="Id">The client-assigned unique identifier of the custom event definition.</param>
/// <param name="SportId">The unique identifier of the target sport.</param>
/// <param name="OwnerId">The unique identifier of the owner user.</param>
/// <param name="Name">The display name of the custom event definition.</param>
/// <param name="ShortName">The abbreviated short code of the custom event definition.</param>
/// <param name="IsPositive">Indicates whether the custom action has a positive statistical impact.</param>
/// <param name="IsShot">Indicates whether the custom action requires goal shot target location tracking.</param>
/// <param name="Score">The score points awarded for this custom event action.</param>
/// <param name="FoulImpact">The disciplinary impact of the custom event on the player.</param>
/// <param name="PenaltyDurationSeconds">The optional team penalty duration in seconds.</param>
public record CreateCustomEventDefinitionCommand(
    Guid Id,
    Guid SportId,
    string OwnerId,
    string Name,
    string ShortName,
    bool IsPositive,
    bool IsShot = false,
    int Score = 0,
    FoulImpact FoulImpact = FoulImpact.None,
    int? PenaltyDurationSeconds = null) : IRequest<EventDefinitionResponse>;

/// <summary>
/// Extension methods for mapping CreateCustomEventDefinitionRequest DTO to CreateCustomEventDefinitionCommand.
/// </summary>
public static class CreateCustomEventDefinitionCommandExtensions
{
    /// <summary>
    /// Maps CreateCustomEventDefinitionRequest DTO to CreateCustomEventDefinitionCommand.
    /// </summary>
    /// <param name="request">The request DTO.</param>
    /// <param name="sportId">The sport ID.</param>
    /// <param name="ownerId">The owner user ID.</param>
    /// <returns>The mapped command.</returns>
    public static CreateCustomEventDefinitionCommand ToCommand(
        this CreateCustomEventDefinitionRequest request,
        Guid sportId,
        string ownerId)
    {
        return new CreateCustomEventDefinitionCommand(
            Id: request.Id,
            SportId: sportId,
            OwnerId: ownerId,
            Name: request.Name,
            ShortName: request.ShortName,
            IsPositive: request.IsPositive,
            IsShot: request.IsShot,
            Score: request.Score,
            FoulImpact: request.FoulImpact,
            PenaltyDurationSeconds: request.PenaltyDurationSeconds);
    }
}