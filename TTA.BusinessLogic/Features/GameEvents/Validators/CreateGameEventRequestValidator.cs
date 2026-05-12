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

        RuleFor(x => x.EventTimestamp)
            .NotEmpty().WithMessage("Event timestamp is required.")
            .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("Event timestamp cannot be in the future.");
    }
}