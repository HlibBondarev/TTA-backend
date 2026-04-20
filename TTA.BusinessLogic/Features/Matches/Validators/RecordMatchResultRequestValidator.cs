using FluentValidation;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Validators;

/// <summary>
/// Validates the <see cref="RecordMatchResultRequest"/> DTO.
/// Ensures scores are non-negative and environmental conditions are within realistic bounds.
/// </summary>
public class RecordMatchResultRequestValidator : AbstractValidator<RecordMatchResultRequest>
{
    /// <summary>
    /// Initializes validation rules for match result recording.
    /// </summary>
    public RecordMatchResultRequestValidator()
    {
        RuleFor(x => x.HomeScore)
            .GreaterThanOrEqualTo(0).WithMessage("Home score cannot be negative.");

        RuleFor(x => x.GuestScore)
            .GreaterThanOrEqualTo(0).WithMessage("Guest score cannot be negative.");

        RuleFor(x => x.Temperature)
            .InclusiveBetween(-50, 60).When(x => x.Temperature.HasValue)
            .WithMessage("Temperature must be between -50 and 60 degrees Celsius.");
    }
}