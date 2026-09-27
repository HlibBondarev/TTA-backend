using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Validators;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Validators;

/// <summary>
/// Unit tests for the <see cref="PlayerTimeInMatchRequestValidator"/> class.
/// Validates constraints and correctness rules for analytical request payloads.
/// </summary>
public class PlayerTimeInMatchRequestValidatorTests
{
    private readonly PlayerTimeInMatchRequestValidator _validator = new();

    /// <summary>
    /// Verifies that the validator does not yield any errors when both MatchId and TeamId are valid, non-empty GUIDs.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new PlayerTimeInMatchRequest(
            MatchId: Guid.NewGuid(),
            TeamId: Guid.NewGuid()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that a validation error is generated when the <see cref="PlayerTimeInMatchRequest.MatchId"/> is an empty GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_MatchIdIsEmpty()
    {
        // Arrange
        var request = new PlayerTimeInMatchRequest(
            MatchId: Guid.Empty,
            TeamId: Guid.NewGuid()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MatchId)
            .WithErrorMessage("MatchId must be a valid, non-empty GUID.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when the <see cref="PlayerTimeInMatchRequest.TeamId"/> is an empty GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_TeamIdIsEmpty()
    {
        // Arrange
        var request = new PlayerTimeInMatchRequest(
            MatchId: Guid.NewGuid(),
            TeamId: Guid.Empty
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TeamId)
            .WithErrorMessage("TeamId must be a valid, non-empty GUID.");
    }
}