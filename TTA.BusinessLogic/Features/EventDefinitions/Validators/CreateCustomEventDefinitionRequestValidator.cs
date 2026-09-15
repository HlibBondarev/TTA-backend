using FluentValidation;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;

namespace TTA.BusinessLogic.Features.EventDefinitions.Validators;

/// <summary>
/// Validator for CreateCustomEventDefinitionRequest DTO.
/// </summary>
public class CreateCustomEventDefinitionRequestValidator : AbstractValidator<CreateCustomEventDefinitionRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateCustomEventDefinitionRequestValidator"/> class.
    /// </summary>

    public CreateCustomEventDefinitionRequestValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Definition ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Name is required.")
            .MaximumLength(50)
            .WithMessage("Name must not exceed 50 characters.");

        RuleFor(x => x.ShortName)
            .NotEmpty()
            .WithMessage("Short name is required.")
            .MaximumLength(10)
            .WithMessage("Short name must not exceed 10 characters.");
    }
}