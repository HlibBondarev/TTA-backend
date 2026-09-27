using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Validators;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Validators;

/// <summary>
/// Unit tests for the <see cref="TerminatePresenceRequestValidator"/> class.
/// </summary>
public class TerminatePresenceRequestValidatorTests
{
    private readonly TerminatePresenceRequestValidator _validator = new();

    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new TerminatePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: new[] { Guid.NewGuid() },
            TimeOut: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new TerminatePresenceRequest(
            PeriodNumber: invalidPeriod,
            PlayerLineupIds: new[] { Guid.NewGuid() },
            TimeOut: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    [Fact]
    public void Validator_Should_HaveError_When_PlayerLineupIdsIsEmpty()
    {
        // Arrange
        var request = new TerminatePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: Array.Empty<Guid>(),
            TimeOut: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PlayerLineupIds)
            .WithErrorMessage("At least one player lineup ID must be provided.");
    }

    [Fact]
    public void Validator_Should_HaveError_When_TimeOutIsDefault()
    {
        // Arrange
        var request = new TerminatePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: new[] { Guid.NewGuid() },
            TimeOut: default
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TimeOut)
            .WithErrorMessage("TimeOut timestamp is required.");
    }

    [Fact]
    public void Validator_Should_HaveError_When_TimeOutIsInFuture()
    {
        // Arrange
        var request = new TerminatePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: [Guid.NewGuid()],
            TimeOut: DateTime.UtcNow.AddMinutes(10)
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TimeOut)
            .WithErrorMessage("TimeOut timestamp cannot be in the future.");
    }
}