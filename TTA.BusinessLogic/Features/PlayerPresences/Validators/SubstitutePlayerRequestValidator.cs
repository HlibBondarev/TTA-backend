using FluentValidation;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;

namespace TTA.BusinessLogic.Features.PlayerPresences.Validators;

/// <summary>
/// Enforces validation rules and data integrity checks for incoming player substitution requests.
/// </summary>
public class SubstitutePlayerRequestValidator : AbstractValidator<SubstitutePlayerRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubstitutePlayerRequestValidator"/> class.
    /// Defines property-level validation rule sets for offline-first player substitution payloads.
    /// </summary>
    public SubstitutePlayerRequestValidator()
    {
        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0).WithMessage("Period number must be greater than zero.");

        RuleFor(x => x.PlayerOutLineupId)
            .NotEmpty().WithMessage("Outgoing player lineup identifier is required.");

        RuleFor(x => x.PlayerInLineupId)
            .NotEmpty().WithMessage("Incoming player lineup identifier is required.")
            .NotEqual(x => x.PlayerOutLineupId).WithMessage("Incoming and outgoing players must be different.");

        RuleFor(x => x.IncomingPresenceId)
            .NotEmpty().WithMessage("Incoming player presence identifier is required.");

        RuleFor(x => x.SubstitutionTime)
            .NotEmpty().WithMessage("Substitution timestamp is required.");
    }
}