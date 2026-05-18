using FluentValidation;
using TTA.BusinessLogic.Features.GameEvents.DTOs;

namespace TTA.BusinessLogic.Features.GameEvents.Validators;

/// <summary>
/// Validator for the <see cref="CreateGameEventRequest"/> record.
/// </summary>
public class CreateGameEventRequestValidator : AbstractValidator<CreateGameEventRequest>
{
    /// <summary>
    /// Initializes validation rules for <see cref="CreateGameEventRequest"/>.
    /// </summary>
    public CreateGameEventRequestValidator()
    {
        RuleFor(x => x.EventDefinitionId)
            .NotEmpty().WithMessage("Event definition is required.");

        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0).WithMessage("Period number must be greater than zero.");

        // Optional: If MatchLineupId is provided, it should not be an empty Guid
        RuleFor(x => x.MatchLineupId)
            .NotEqual(Guid.Empty)
            .WithMessage("MatchLineupId cannot be an empty GUID.");
    }
}