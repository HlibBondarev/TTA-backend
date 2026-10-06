using FluentValidation;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;

namespace TTA.BusinessLogic.Features.PlayerPresences.Validators;

/// <summary>
/// Validator for the <see cref="CreatePlayerPresenceRequest"/> record.
/// </summary>
public class CreatePlayerPresenceRequestValidator : AbstractValidator<CreatePlayerPresenceRequest>
{
    /// <summary>
    /// Initializes validation rules for <see cref="CreatePlayerPresenceRequest"/>.
    /// </summary>
    public CreatePlayerPresenceRequestValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Player presence ID is required.");

        RuleFor(x => x.MatchLineupId)
            .NotEmpty().WithMessage("MatchLineupId cannot be an empty GUID.");

        RuleFor(x => x.PeriodNumber)
            .GreaterThan(0).WithMessage("Period number must be greater than zero.");

        RuleFor(x => x.TimeIn)
            .NotEmpty().WithMessage("TimeIn is required.")
            .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5))
            .WithMessage("TimeIn cannot be in the future.");

        RuleFor(x => x.TimeOut)
            .GreaterThan(x => x.TimeIn)
            .When(x => x.TimeOut.HasValue)
            .WithMessage("TimeOut must be later than TimeIn.");
    }
}