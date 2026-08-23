using FluentValidation;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Validators;

/// <summary>
/// Validator for <see cref="AddUserToTrackedMatchRequest"/>.
/// Ensures that email address is provided and correctly formatted.
/// </summary>
public class AddUserToTrackedMatchRequestValidator : AbstractValidator<AddUserToTrackedMatchRequest>
{
    /// <summary>
    /// Initializes validation rules for user match sharing requests.
    /// </summary>
    public AddUserToTrackedMatchRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("User email is required.")
            .EmailAddress()
            .WithMessage("A valid email address must be provided.");
    }
}