using FluentValidation;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;

namespace TTA.BusinessLogic.Features.PlayerPresences.Validators;

/// <summary>
/// Validates boundaries and completeness rules for the <see cref="PlayerTimeInMatchRequest"/> DTO.
/// </summary>
public class PlayerTimeInMatchRequestValidator : AbstractValidator<PlayerTimeInMatchRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerTimeInMatchRequestValidator"/> class with specific validation rules.
    /// </summary>
    public PlayerTimeInMatchRequestValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("MatchId must be a valid, non-empty GUID.");

        RuleFor(x => x.TeamId)
            .NotEmpty()
            .WithMessage("TeamId must be a valid, non-empty GUID.");
    }
}