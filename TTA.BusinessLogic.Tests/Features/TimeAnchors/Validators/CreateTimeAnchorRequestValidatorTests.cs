using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.Validators;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Tests.Features.TimeAnchors.Validators;

/// <summary>
/// Unit tests for the <see cref="CreateTimeAnchorRequestValidator"/> class.
/// Validates business rules for creating a new time anchor request.
/// </summary>
public class CreateTimeAnchorRequestValidatorTests
{
    private readonly CreateTimeAnchorRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateTimeAnchorRequestValidatorTests"/> class.
    /// </summary>
    public CreateTimeAnchorRequestValidatorTests()
    {
        _validator = new CreateTimeAnchorRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator does not return any errors for a valid request.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateTimeAnchorRequest.Id"/> is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_IdIsEmpty()
    {
        // Arrange
        var request = new CreateTimeAnchorRequest(
            Id: Guid.Empty,
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Time anchor ID is required.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateTimeAnchorRequest.PeriodNumber"/> is zero or negative.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: invalidPeriod,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateTimeAnchorRequest.Type"/> contains an invalid enum value.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_TypeIsInvalid()
    {
        // Arrange
        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: (TimeAnchorType)999,
            Timestamp: DateTime.UtcNow
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Type)
            .WithErrorMessage("Invalid time anchor type provided.");
    }

    /// <summary>
    /// Verifies that an error is returned when <see cref="CreateTimeAnchorRequest.Timestamp"/> is default/empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_TimestampIsDefault()
    {
        // Arrange
        var request = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: default
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Timestamp)
            .WithErrorMessage("Timestamp is required.");
    }
}