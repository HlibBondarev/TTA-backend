using FluentValidation;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Validators;

/// <summary>
/// Validates the <see cref="RecordMatchResultRequest"/> DTO.
/// </summary>
public class RecordMatchResultRequestValidator : AbstractValidator<RecordMatchResultRequest>
{
    public RecordMatchResultRequestValidator()
    {
        RuleFor(x => x.HomeScore)
            .NotNull().WithMessage("Home score is required.")
            .GreaterThanOrEqualTo(0).WithMessage("Home score cannot be negative.");

        RuleFor(x => x.GuestScore)
            .NotNull().WithMessage("Guest score is required.")
            .GreaterThanOrEqualTo(0).WithMessage("Guest score cannot be negative.");

        RuleFor(x => x.Temperature)
            .InclusiveBetween(-50, 60).When(x => x.Temperature.HasValue)
            .WithMessage("Temperature must be between -50 and 60 degrees Celsius.");
    }
}