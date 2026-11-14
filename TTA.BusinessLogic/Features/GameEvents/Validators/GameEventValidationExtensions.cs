using FluentValidation;

namespace TTA.BusinessLogic.Features.GameEvents.Validators;

/// <summary>
/// Reusable validation rules for game event coordinate properties.
/// </summary>
public static class GameEventValidationExtensions
{
    /// <summary>
    /// Configures range (0.00 - 100.00) and precision (max 2 decimal places) validation rules for coordinate values.
    /// </summary>
    public static IRuleBuilderOptions<T, decimal?> ValidCoordinate<T>(
        this IRuleBuilder<T, decimal?> ruleBuilder,
        string propertyName)
    {
        return ruleBuilder
            .InclusiveBetween(0.00m, 100.00m)
            .WithMessage($"{propertyName} must be between 0.00 and 100.00.")
            .PrecisionScale(5, 2, ignoreTrailingZeros: true)
            .WithMessage($"{propertyName} cannot have more than 2 decimal places.");
    }
}