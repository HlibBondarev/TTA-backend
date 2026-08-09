using FluentValidation;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;

namespace TTA.BusinessLogic.Features.PlayerPresences.Validators;

/// <summary>
/// Validator for the <see cref="TerminatePresenceRequest"/> record.
/// </summary>
public class TerminatePresenceRequestValidator : AbstractValidator<TerminatePresenceRequest>
{
    /// <summary>
    /// Initializes validation rules for <see cref="TerminatePresenceRequest"/>.
    /// </summary>
    public TerminatePresenceRequestValidator()
    {
        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0)
            .WithMessage("Period number must be greater than zero.");

        RuleFor(x => x.PlayerLineupIds)
            .NotNull()
            .WithMessage("Player lineup IDs collection cannot be null.")
            .Must(x => x != null && x.Any())
            .WithMessage("At least one player lineup ID must be provided.");

        RuleForEach(x => x.PlayerLineupIds)
            .NotEmpty()
            .WithMessage("Player lineup ID cannot be empty.");

        RuleFor(x => x.TimeOut)
            .NotEmpty()
            .WithMessage("TimeOut timestamp is required.")
            .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5))
            .WithMessage("TimeOut timestamp cannot be in the future.");
    }
}