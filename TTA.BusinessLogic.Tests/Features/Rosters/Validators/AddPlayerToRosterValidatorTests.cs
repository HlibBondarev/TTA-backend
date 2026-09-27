using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Rosters.DTOs;
using TTA.BusinessLogic.Features.Rosters.Validators;

namespace TTA.BusinessLogic.Tests.Features.Rosters.Validators;

/// <summary>
/// Unit tests for <see cref="AddPlayerToRosterRequestValidator"/> ensuring all 
/// business rules for roster requests are strictly enforced.
/// </summary>
public class AddPlayerToRosterValidatorTests
{
    private readonly AddPlayerToRosterRequestValidator _validator = new();

    /// <summary>
    /// Verifies that a valid request passes all validation rules.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new AddPlayerToRosterRequest(
            PlayerId: Guid.NewGuid(),
            PositionId: Guid.NewGuid(),
            Number: 10
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an empty PlayerId triggers a validation error.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_PlayerId_Is_Empty()
    {
        // Arrange
        var request = new AddPlayerToRosterRequest(Guid.Empty, Guid.NewGuid(), 10);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PlayerId)
            .WithErrorMessage("Player identifier is required.");
    }

    /// <summary>
    /// Verifies that an empty PositionId triggers a validation error.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_PositionId_Is_Empty()
    {
        // Arrange
        var request = new AddPlayerToRosterRequest(Guid.NewGuid(), Guid.Empty, 10);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PositionId)
            .WithErrorMessage("Position identifier is required.");
    }

    /// <summary>
    /// Verifies that a jersey number below the allowed range triggers an error.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Should_Have_Error_When_Number_Is_Out_Of_Range(int invalidNumber)
    {
        // Arrange
        var request = new AddPlayerToRosterRequest(Guid.NewGuid(), Guid.NewGuid(), invalidNumber);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Number)
            .WithErrorMessage("Jersey number must be between 0 and 99.");
    }

    /// <summary>
    /// Verifies that boundary values for the jersey number are considered valid.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Should_Not_Have_Error_When_Number_Is_On_Boundary(int boundaryNumber)
    {
        // Arrange
        var request = new AddPlayerToRosterRequest(Guid.NewGuid(), Guid.NewGuid(), boundaryNumber);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Number);
    }
}