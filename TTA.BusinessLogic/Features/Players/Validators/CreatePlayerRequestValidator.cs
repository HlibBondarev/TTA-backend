using FluentValidation;
using TTA.BusinessLogic.Features.Players.DTOs;

namespace TTA.BusinessLogic.Features.Players.Validators;

/// <summary>
/// Validates the <see cref="CreatePlayerRequest"/> DTO before it is processed by the controller.
/// </summary>
public class CreatePlayerRequestValidator : AbstractValidator<CreatePlayerRequest>
{
    public CreatePlayerRequestValidator()
    {
        RuleFor(x => x.HomeClubId)
            .NotEmpty().WithMessage("Home Club ID is required.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name cannot exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name cannot exceed 100 characters.");

        RuleFor(x => x.BirthDate)
            .NotEmpty().WithMessage("Birth date is required.")
            .LessThan(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Birth date must be in the past.")
            .Must(BeReasonableAge).WithMessage("Player age must be between 5 and 100 years.");

        RuleFor(x => x.Gender)
            .IsInEnum().WithMessage("Invalid gender value. Use 0 for Male or 1 for Female.");
    }

    private static bool BeReasonableAge(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;

        return age is >= 5 and <= 100;
    }
}