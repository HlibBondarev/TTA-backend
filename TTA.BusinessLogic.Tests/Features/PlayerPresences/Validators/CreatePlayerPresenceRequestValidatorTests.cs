using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Validators;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Validators;

/// <summary>
/// Unit tests for the <see cref="CreatePlayerPresenceRequestValidator"/> class.
/// </summary>
public class CreatePlayerPresenceRequestValidatorTests
{
    private readonly CreatePlayerPresenceRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreatePlayerPresenceRequestValidatorTests"/> class.
    /// </summary>
    public CreatePlayerPresenceRequestValidatorTests()
    {
        _validator = new CreatePlayerPresenceRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator does not return any errors for a valid player presence request.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new CreatePlayerPresenceRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow.AddMinutes(-5),
            TimeOut: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreatePlayerPresenceRequest.Id"/> is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_IdIsEmpty()
    {
        // Arrange
        var request = new CreatePlayerPresenceRequest(
            Id: Guid.Empty,
            MatchLineupId: Guid.NewGuid(),
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            TimeOut: null
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Player presence ID is required.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreatePlayerPresenceRequest.MatchLineupId"/> is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_MatchLineupIdIsEmpty()
    {
        // Arrange
        var request = new CreatePlayerPresenceRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.Empty,
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            TimeOut: null
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MatchLineupId)
            .WithErrorMessage("MatchLineupId cannot be an empty GUID.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreatePlayerPresenceRequest.PeriodNumber"/> is invalid.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new CreatePlayerPresenceRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            PeriodNumber: invalidPeriod,
            TimeIn: DateTime.UtcNow,
            TimeOut: null
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreatePlayerPresenceRequest.TimeOut"/> is earlier than <see cref="CreatePlayerPresenceRequest.TimeIn"/>.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_TimeOutIsEarlierThanTimeIn()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var request = new CreatePlayerPresenceRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            PeriodNumber: 1,
            TimeIn: now,
            TimeOut: now.AddMinutes(-5)
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TimeOut)
            .WithErrorMessage("TimeOut must be later than TimeIn.");
    }
}