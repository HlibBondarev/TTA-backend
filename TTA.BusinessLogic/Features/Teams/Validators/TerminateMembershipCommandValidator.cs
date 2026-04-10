using FluentValidation;
using TTA.BusinessLogic.Features.Teams.Commands;

namespace TTA.BusinessLogic.Features.Teams.Validators;

public class TerminateMembershipCommandValidator : AbstractValidator<TerminateMembershipCommand>
{
    public TerminateMembershipCommandValidator()
    {
        RuleFor(x => x.UserEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.RoleInTeam).IsInEnum();

        RuleFor(x => x.LeftAt)
            .Must(date => !date.HasValue || date.Value >= DateTime.UtcNow)
            .WithMessage("Termination date cannot be in the past.");
    }
}