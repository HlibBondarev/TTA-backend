using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Validators;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Validators;

/// <summary>
/// Unit tests for <see cref="UpdatePlayerInMatchLineupRequestValidator"/>.
/// Ensures that updates to match protocol entries maintain data integrity.
/// </summary>
public class UpdatePlayerInMatchLineupRequestValidatorTests
{
    private readonly UpdatePlayerInMatchLineupRequestValidator _validator;

    public UpdatePlayerInMatchLineupRequestValidatorTests()
    {
        _validator = new UpdatePlayerInMatchLineupRequestValidator();
    }

    /// <summary>
    /// Validates that an empty PositionId triggers a validation error.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_PositionId_Is_Empty()
    {
        // Arrange
        var request = new UpdatePlayerInMatchLineupRequest(10, true, Guid.Empty);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PositionId)
            .WithErrorMessage("Position identifier is required.");
    }

    /// <summary>
    /// Validates that jersey numbers outside the 0-99 range trigger errors.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Should_Have_Error_When_Number_Is_Out_Of_Range(int invalidNumber)
    {
        // Arrange
        var request = new UpdatePlayerInMatchLineupRequest(invalidNumber, true, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Number)
            .WithErrorMessage("Jersey number must be between 0 and 99.");
    }

    /// <summary>
    /// Validates that jersey numbers within the allowed range (0-99) pass validation.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(23)]
    public void Should_Not_Have_Error_When_Number_Is_Within_Range(int validNumber)
    {
        // Arrange
        var request = new UpdatePlayerInMatchLineupRequest(validNumber, true, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Number);
    }

    /// <summary>
    /// Verifies that a fully valid update request passes all validation rules.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new UpdatePlayerInMatchLineupRequest(11, true, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}