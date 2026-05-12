using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Validators;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Validators;

/// <summary>
/// Unit tests for the <see cref="UpdateGameEventRequestValidator"/> class.
/// Validates rules for updating existing game event data.
/// </summary>
public class UpdateGameEventRequestValidatorTests
{
    private readonly UpdateGameEventRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateGameEventRequestValidatorTests"/> class.
    /// </summary>
    public UpdateGameEventRequestValidatorTests()
    {
        _validator = new UpdateGameEventRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator does not return any errors for a valid update request.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new UpdateGameEventRequest(
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 2,
            IsLeadToGoal: true
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="UpdateGameEventRequest.EventDefinitionId"/> is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_EventDefinitionIdIsEmpty()
    {
        // Arrange
        var request = new UpdateGameEventRequest(
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.Empty,
            PeriodNumber: 1,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.EventDefinitionId)
            .WithErrorMessage("Event definition is required.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="UpdateGameEventRequest.PeriodNumber"/> is not positive.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new UpdateGameEventRequest(
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: invalidPeriod,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="UpdateGameEventRequest.MatchLineupId"/> 
    /// is explicitly set to an empty GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_MatchLineupIdIsEmpty()
    {
        // Arrange
        var request = new UpdateGameEventRequest(
            MatchLineupId: Guid.Empty,
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MatchLineupId)
            .WithErrorMessage("MatchLineupId cannot be an empty GUID.");
    }
}