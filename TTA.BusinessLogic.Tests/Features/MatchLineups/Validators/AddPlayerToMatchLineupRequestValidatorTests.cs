using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.MatchLineups.DTOs;
using TTA.BusinessLogic.Features.MatchLineups.Validators;

namespace TTA.BusinessLogic.Tests.Features.MatchLineups.Validators;

/// <summary>
/// Unit tests for <see cref="AddPlayerToMatchLineupRequestValidator"/>.
/// Tests validation rules for position identifiers and jersey numbers.
/// </summary>
public class AddPlayerToMatchLineupRequestValidatorTests
{
    private readonly AddPlayerToMatchLineupRequestValidator _validator;

    public AddPlayerToMatchLineupRequestValidatorTests()
    {
        _validator = new AddPlayerToMatchLineupRequestValidator();
    }

    [Fact]
    public void Should_Have_Error_When_PositionId_Is_Empty()
    {
        // Arrange
        var request = new AddPlayerToMatchLineupRequest(10, true, Guid.Empty);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PositionId)
            .WithErrorMessage("Position identifier is required.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Should_Have_Error_When_Number_Is_Out_Of_Range(int invalidNumber)
    {
        // Arrange
        var request = new AddPlayerToMatchLineupRequest(invalidNumber, true, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Number)
            .WithErrorMessage("Jersey number must be between 0 and 99.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(45)]
    public void Should_Not_Have_Error_When_Number_Is_Within_Range(int validNumber)
    {
        // Arrange
        var request = new AddPlayerToMatchLineupRequest(validNumber, true, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Number);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new AddPlayerToMatchLineupRequest(7, false, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}
