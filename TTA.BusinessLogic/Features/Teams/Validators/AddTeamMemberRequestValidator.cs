using FluentValidation;
using TTA.BusinessLogic.Features.Teams.DTOs;

namespace TTA.BusinessLogic.Features.Teams.Validators;

/// <summary>
/// Validates the <see cref="AddTeamMemberRequest"/> DTO.
/// </summary>
public class AddTeamMemberRequestValidator : AbstractValidator<AddTeamMemberRequest>
{
    public AddTeamMemberRequestValidator()
    {
        RuleFor(x => x.UserEmail)
            .NotEmpty().WithMessage("User email is required.")
            .EmailAddress().WithMessage("User email must be a valid email address.");

        RuleFor(x => x.RoleInTeam)
            .IsInEnum().WithMessage("A valid team role must be specified.");
    }
}