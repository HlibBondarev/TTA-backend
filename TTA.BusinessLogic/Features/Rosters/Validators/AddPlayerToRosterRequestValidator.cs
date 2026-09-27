using FluentValidation;
using TTA.BusinessLogic.Features.Rosters.DTOs;

namespace TTA.BusinessLogic.Features.Rosters.Validators;

/// <summary>
/// Validator for <see cref="AddPlayerToRosterRequest"/> to ensure data integrity before processing.
/// </summary>
public class AddPlayerToRosterRequestValidator : AbstractValidator<AddPlayerToRosterRequest>
{
    /// <summary>
    /// Initializes validation rules for the roster request.
    /// </summary>
    public AddPlayerToRosterRequestValidator()
    {
        RuleFor(x => x.PlayerId)
            .NotEmpty().WithMessage("Player identifier is required.");

        RuleFor(x => x.PositionId)
            .NotEmpty().WithMessage("Position identifier is required.");

        RuleFor(x => x.Number)
            .InclusiveBetween(0, 99).WithMessage("Jersey number must be between 0 and 99.");
    }
}