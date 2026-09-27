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

        RuleFor(x => x.TimeIn)
            .NotEmpty().WithMessage("TimeIn timestamp must be specified.");

        RuleFor(x => x.PresenceItems)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("At least one player presence item must be provided for initialization.")
            .Must(items => items.Select(i => i.MatchLineupId).Distinct().Count() == items.Count())
            .WithMessage("Player lineup identifiers must be unique within the request.")
            .Must(items => items.Select(i => i.Id).Distinct().Count() == items.Count())
            .WithMessage("Presence identifiers must be unique within the request.");

        RuleForEach(x => x.PresenceItems)
            .ChildRules(item =>
            {
                item.RuleFor(i => i.Id)
                    .NotEmpty().WithMessage("Presence identifier cannot be an empty GUID.");

                item.RuleFor(i => i.MatchLineupId)
                    .NotEmpty().WithMessage("Player lineup identifier cannot be an empty GUID.");
            });
    }
}