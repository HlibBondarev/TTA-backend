using FluentValidation;
using TTA.BusinessLogic.Features.Clubs.DTOs;

namespace TTA.BusinessLogic.Features.Clubs.Validators;

/// <summary>
/// Validates the DTO before it enters the Controller's Action Method.
/// </summary>
public class CreateClubRequestValidator : AbstractValidator<CreateClubRequest>
{
    public CreateClubRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Club name is required.")
            .MinimumLength(3).WithMessage("Name must be at least 3 characters long.")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");

        RuleFor(x => x.CityId)
            .NotEmpty().WithMessage("City must be selected.");
    }
}