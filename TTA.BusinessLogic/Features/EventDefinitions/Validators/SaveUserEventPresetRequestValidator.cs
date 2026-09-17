using FluentValidation;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;

namespace TTA.BusinessLogic.Features.EventDefinitions.Validators;

/// <summary>
/// Validator for SaveUserEventPresetRequest DTO.
/// </summary>
public class SaveUserEventPresetRequestValidator : AbstractValidator<SaveUserEventPresetRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SaveUserEventPresetRequestValidator"/> class.
    /// </summary>

    public SaveUserEventPresetRequestValidator()
    {
        RuleFor(x => x.EventDefinitionIds)
            .NotNull()
            .WithMessage("Event definition IDs collection cannot be null.")
            .Must(x => x == null || x.Distinct().Count() == x.Count())
            .WithMessage("Event definition IDs collection cannot contain duplicate values.");
    }
}