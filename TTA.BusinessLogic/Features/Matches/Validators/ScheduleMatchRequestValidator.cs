using FluentValidation;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Validators;

/// <summary>
/// Validates the <see cref="ScheduleMatchRequest"/> DTO before it is processed by the controller.
/// </summary>
public class ScheduleMatchRequestValidator : AbstractValidator<ScheduleMatchRequest>
{
    public ScheduleMatchRequestValidator()
    {
        RuleFor(x => x.HomeTeamId)
            .NotEmpty().WithMessage("The home team must be selected.");

        RuleFor(x => x.GuestTeamId)
            .NotEmpty().WithMessage("The guest team must be selected.");

        RuleFor(x => x.GuestTeamId)
            .NotEmpty().WithMessage("The guest team must be selected.")
            .Must((req, guestTeamId) => guestTeamId != req.HomeTeamId)
            .WithMessage("The guest team must be different from the home team.");

        RuleFor(x => x.ScheduledAt)
            .NotEmpty().WithMessage("The scheduled date and time is required.")
            .GreaterThan(DateTime.UtcNow).WithMessage("The match must be scheduled for a future date and time.");

        RuleFor(x => x.MatchNumber)
            .MaximumLength(50).WithMessage("Match number cannot exceed 50 characters.");

        RuleFor(x => x.Venue)
            .MaximumLength(200).WithMessage("Venue cannot exceed 200 characters.");
    }
}