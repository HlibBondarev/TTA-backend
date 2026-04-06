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
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User identifier is required.");

        RuleFor(x => x.RoleInTeam)
            .IsInEnum().WithMessage("A valid team role must be specified.");
    }
}