using FluentValidation;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;

namespace TTA.BusinessLogic.Features.TimeAnchors.Validators;

/// <summary>
/// Validator for the <see cref="CreateTimeAnchorRequest"/> record.
/// </summary>
public class CreateTimeAnchorRequestValidator : AbstractValidator<CreateTimeAnchorRequest>
{
    /// <summary>
    /// Initializes validation rules for <see cref="CreateTimeAnchorRequest"/>.
    /// </summary>
    public CreateTimeAnchorRequestValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Time anchor ID is required.");

        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0)
            .WithMessage("Period number must be greater than zero.");

        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Invalid time anchor type provided.");

        RuleFor(x => x.Timestamp)
            .NotEmpty()
            .WithMessage("Timestamp is required.")
            .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5))
            .WithMessage("Timestamp cannot be in the future.");
    }
}