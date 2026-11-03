using FluentValidation;
using TTA.BusinessLogic.Features.GameEvents.DTOs;

namespace TTA.BusinessLogic.Features.GameEvents.Validators;

/// <summary>
/// Validator for the <see cref="UpdateGameEventRequest"/> record.
/// </summary>
public class UpdateGameEventRequestValidator : AbstractValidator<UpdateGameEventRequest>
{
    /// <summary>
    /// Initializes validation rules for <see cref="UpdateGameEventRequest"/>.
    /// </summary>
    public UpdateGameEventRequestValidator()
    {
        RuleFor(x => x.EventDefinitionId)
            .NotEmpty().WithMessage("Event definition is required.");

        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0).WithMessage("Period number must be greater than zero.");

        RuleFor(x => x.MatchLineupId)
            .NotEmpty().WithMessage("MatchLineupId cannot be an empty GUID.");

        RuleFor(x => x.LocationX).ValidCoordinate("LocationX");
        RuleFor(x => x.LocationY).ValidCoordinate("LocationY");
        RuleFor(x => x.ShotLocationX).ValidCoordinate("ShotLocationX");
        RuleFor(x => x.ShotLocationY).ValidCoordinate("ShotLocationY");
    }
}