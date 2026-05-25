using FluentValidation;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;

namespace TTA.BusinessLogic.Features.PlayerPresences.Validators;

/// <summary>
/// Enforces validation rules and data integrity checks for incoming presence initialization requests.
/// </summary>
public class InitializePresenceRequestValidator : AbstractValidator<InitializePresenceRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InitializePresenceRequestValidator"/> class.
    /// Defines property-level validation rule sets.
    /// </summary>
    public InitializePresenceRequestValidator()
    {
        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0).WithMessage("Period number must be greater than zero.");

        RuleFor(x => x.PlayerLineupIds)
            .NotEmpty().WithMessage("At least one player lineup identifier must be provided for initialization.")
            .Must(ids => ids.Distinct().Count() == ids.Count()).WithMessage("Player lineup identifiers must be unique.");

        RuleForEach(x => x.PlayerLineupIds)
            .NotEmpty().WithMessage("Player lineup identifiers cannot be empty GUIDs.");
    }
}