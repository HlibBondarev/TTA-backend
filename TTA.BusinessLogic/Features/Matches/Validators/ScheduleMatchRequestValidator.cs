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
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((request, guestId) => guestId != request.HomeTeamId)
            .WithMessage("The guest team must be different from the home team.");

        RuleFor(x => x.ScheduledAt)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .GreaterThan(DateTime.UtcNow)
            .WithMessage("Match must be scheduled in the future.");

        RuleFor(x => x.MatchNumber)
            .MaximumLength(50).WithMessage("Match number cannot exceed 50 characters.");

        RuleFor(x => x.Venue)
            .MaximumLength(200).WithMessage("Venue cannot exceed 200 characters.");
    }
}