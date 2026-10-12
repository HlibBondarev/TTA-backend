using FluentValidation;
using TTA.BusinessLogic.Features.GameEvents.DTOs;

namespace TTA.BusinessLogic.Features.GameEvents.Validators;

/// <summary>
/// Validator for the <see cref="UpdateGameEventRequest"/> record.
/// </summary>
public class UpdateGameEventRequestValidator : AbstractValidator<UpdateGameEventRequest>
{
    public UpdateGameEventRequestValidator()
    {
        RuleFor(x => x.EventDefinitionId)
            .NotEmpty().WithMessage("Event definition is required.");

        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0).WithMessage("Period number must be greater than zero.");

        RuleFor(x => x.MatchLineupId)
            .NotEqual(Guid.Empty)
            .WithMessage("MatchLineupId cannot be an empty GUID.");

        RuleFor(x => x.LocationX)
            .InclusiveBetween(0.00m, 100.00m)
            .WithMessage("LocationX must be between 0.00 and 100.00.");

        RuleFor(x => x.LocationY)
            .InclusiveBetween(0.00m, 100.00m)
            .WithMessage("LocationY must be between 0.00 and 100.00.");
    }
}