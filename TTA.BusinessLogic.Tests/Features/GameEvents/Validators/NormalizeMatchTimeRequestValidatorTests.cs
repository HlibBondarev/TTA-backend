using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Validators;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Validators;

/// <summary>
/// Unit tests for the <see cref="NormalizeMatchTimeRequestValidator"/> class.
/// Validates structural validation rules for mass event time normalization input metrics.
/// </summary>
public class NormalizeMatchTimeRequestValidatorTests
{
    private readonly NormalizeMatchTimeRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="NormalizeMatchTimeRequestValidatorTests"/> class.
    /// </summary>
    public NormalizeMatchTimeRequestValidatorTests()
    {
        _validator = new NormalizeMatchTimeRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator does not return any errors when a valid request 
    /// containing fully initialized GUIDs is provided.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new NormalizeMatchTimeRequest(
            MatchId: Guid.NewGuid(),
            TeamId: Guid.NewGuid()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that a validation error is triggered when the <see cref="NormalizeMatchTimeRequest.MatchId"/> 
    /// property is explicitly configured as an empty or default GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_MatchIdIsEmpty()
    {
        // Arrange
        var request = new NormalizeMatchTimeRequest(
            MatchId: Guid.Empty,
            TeamId: Guid.NewGuid()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MatchId)
            .WithErrorMessage("Match identifier is required and cannot be an empty GUID.");
    }

    /// <summary>
    /// Verifies that a validation error is triggered when the <see cref="NormalizeMatchTimeRequest.TeamId"/> 
    /// property is explicitly configured as an empty or default GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_TeamIdIsEmpty()
    {
        // Arrange
        var request = new NormalizeMatchTimeRequest(
            MatchId: Guid.NewGuid(),
            TeamId: Guid.Empty
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TeamId)
            .WithErrorMessage("Team identifier is required and cannot be an empty GUID.");
    }
}