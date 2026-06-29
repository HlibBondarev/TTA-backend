using FluentValidation;
using TTA.BusinessLogic.Features.GameEvents.DTOs;

namespace TTA.BusinessLogic.Features.GameEvents.Validators;

/// <summary>
/// Validates the structural and business integrity constraints of the <see cref="NormalizeMatchTimeRequest"/>.
/// </summary>
public class NormalizeMatchTimeRequestValidator : AbstractValidator<NormalizeMatchTimeRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NormalizeMatchTimeRequestValidator"/> class.
    /// Defines rules for route parameters to prevent processing empty or malformed identifiers.
    /// </summary>
    public NormalizeMatchTimeRequestValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match identifier is required and cannot be an empty GUID.");

        RuleFor(x => x.TeamId)
            .NotEmpty()
            .WithMessage("Team identifier is required and cannot be an empty GUID.");
    }
}