using FluentValidation;
using TTA.BusinessLogic.Features.Teams.DTOs;

namespace TTA.BusinessLogic.Features.Teams.Validators;

public class TerminateMembershipRequestValidator : AbstractValidator<TerminateMembershipRequest>
{
    public TerminateMembershipRequestValidator()
    {
        RuleFor(x => x.UserEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.RoleInTeam).IsInEnum();

        RuleFor(x => x.LeftAt)
            .Must(date => !date.HasValue || date.Value >= DateTime.UtcNow)
            .WithMessage("Termination date cannot be in the past.");
    }
}