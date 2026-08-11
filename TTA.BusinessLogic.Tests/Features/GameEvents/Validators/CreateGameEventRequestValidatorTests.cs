using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.GameEvents.Validators;

namespace TTA.BusinessLogic.Tests.Features.GameEvents.Validators;

/// <summary>
/// Unit tests for the <see cref="CreateGameEventRequestValidator"/> class.
/// Validates business rules for creating a new game event request.
/// </summary>
public class CreateGameEventRequestValidatorTests
{
    private readonly CreateGameEventRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateGameEventRequestValidatorTests"/> class.
    /// </summary>
    public CreateGameEventRequestValidatorTests()
    {
        _validator = new CreateGameEventRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator does not return any errors for a valid request with current or past timestamp.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow.AddMinutes(-10),
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateGameEventRequest.Id"/> is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_IdIsEmpty()
    {
        // Arrange
        var request = new CreateGameEventRequest(
            Id: Guid.Empty,
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Game event ID is required.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateGameEventRequest.EventDefinitionId"/> is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_EventDefinitionIdIsEmpty()
    {
        // Arrange
        var request = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.Empty,
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.EventDefinitionId)
            .WithErrorMessage("Event definition is required.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateGameEventRequest.MatchLineupId"/> is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_MatchLineupIdIsEmpty()
    {
        // Arrange
        var request = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.Empty,
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MatchLineupId)
            .WithErrorMessage("MatchLineupId cannot be an empty GUID.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateGameEventRequest.PeriodNumber"/> is zero or negative.
    /// </summary>
    /// <param name="invalidPeriod">The invalid period number value to test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: invalidPeriod,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateGameEventRequest.EventTimestamp"/> is default/empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_EventTimestampIsDefault()
    {
        // Arrange
        var request = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: default,
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.EventTimestamp)
            .WithErrorMessage("EventTimestamp is required.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateGameEventRequest.EventTimestamp"/> is set too far in the future.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_EventTimestampIsInFuture()
    {
        // Arrange
        var request = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow.AddMinutes(10),
            IsLeadToGoal: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.EventTimestamp)
            .WithErrorMessage("EventTimestamp cannot be in the future.");
    }
}