using FluentValidation;
using TTA.BusinessLogic.Features.Teams.DTOs;

namespace TTA.BusinessLogic.Features.Teams.Validators;

/// <summary>
/// Validator for <see cref="CreateTeamRequest"/> to ensure data integrity before processing.
/// </summary>
public class CreateTeamRequestValidator : AbstractValidator<CreateTeamRequest>
{
    public CreateTeamRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Team name is required.")
            .MinimumLength(3).WithMessage("Team name must be at least 3 characters long.")
            .MaximumLength(100).WithMessage("Team name must not exceed 100 characters.");

        RuleFor(x => x.SportId)
            .NotEmpty().WithMessage("Sport must be selected.");

        RuleFor(x => x.MinBirthYear)
            .InclusiveBetween(1900, DateTime.UtcNow.Year)
            .When(x => x.MinBirthYear.HasValue)
            .WithMessage("Please enter a valid birth year.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("A valid gender category must be selected.");
    }
}