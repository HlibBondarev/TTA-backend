using FluentValidation;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;

namespace TTA.BusinessLogic.Features.MatchLineups.Validators;

/// <summary>
/// Validator for <see cref="UpdatePlayerInMatchLineupRequest"/> to ensure data integrity 
/// when modifying an existing player entry in the match protocol.
/// </summary>
public class UpdatePlayerInMatchLineupRequestValidator : AbstractValidator<UpdatePlayerInMatchLineupRequest>
{
    /// <summary>
    /// Initializes validation rules for match lineup updates.
    /// </summary>
    public UpdatePlayerInMatchLineupRequestValidator()
    {
        RuleFor(x => x.PositionId)
            .NotEmpty().WithMessage("Position identifier is required.");

        RuleFor(x => x.Number)
            .InclusiveBetween(0, 99).WithMessage("Jersey number must be between 0 and 99.");
    }
}