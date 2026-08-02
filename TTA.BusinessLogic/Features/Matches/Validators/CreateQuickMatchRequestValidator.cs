using FluentValidation;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Validators;

/// <summary>
/// Provides FluentValidation rules for <see cref="CreateQuickMatchRequest"/>.
/// </summary>
public class CreateQuickMatchRequestValidator : AbstractValidator<CreateQuickMatchRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateQuickMatchRequestValidator"/> class.
    /// </summary>
    public CreateQuickMatchRequestValidator()
    {
        RuleFor(x => x.SportId)
            .NotEmpty()
            .WithMessage("SportId is required and cannot be an empty GUID.");
    }
}