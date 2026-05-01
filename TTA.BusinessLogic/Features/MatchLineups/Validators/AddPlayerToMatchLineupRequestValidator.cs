using FluentValidation;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;

namespace TTA.BusinessLogic.Features.MatchLineups.Validators;

/// <summary>
/// Validator for <see cref="AddPlayerToMatchLineupRequest"/> to ensure data integrity 
/// when adding a player to a match protocol.
/// </summary>
public class AddPlayerToMatchLineupRequestValidator : AbstractValidator<AddPlayerToMatchLineupRequest>
{
    /// <summary>
    /// Initializes validation rules for match lineup creation.
    /// </summary>
    public AddPlayerToMatchLineupRequestValidator()
    {
        RuleFor(x => x.PositionId)
            .NotEmpty().WithMessage("Position identifier is required.");

        RuleFor(x => x.Number)
            .InclusiveBetween(0, 99).WithMessage("Jersey number must be between 0 and 99.");
    }
}