using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Validators;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Validators;

/// <summary>
/// Unit tests for the <see cref="InitializePresenceRequestValidator"/> class.
/// Validates business rules for incoming presence initialization requests.
/// </summary>
public class InitializePresenceRequestValidatorTests
{
    private readonly InitializePresenceRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="InitializePresenceRequestValidatorTests"/> class.
    /// </summary>
    public InitializePresenceRequestValidatorTests()
    {
        _validator = new InitializePresenceRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator does not return any errors for a perfectly valid request payload.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="InitializePresenceRequest.PeriodNumber"/> is zero or negative.
    /// </summary>
    /// <param name="invalidPeriod">The invalid period sequence number supplied by the InlineData attribute.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: invalidPeriod,
            PlayerLineupIds: new List<Guid> { Guid.NewGuid() }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="InitializePresenceRequest.PlayerLineupIds"/> collection is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PlayerLineupIdsIsEmpty()
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: new List<Guid>() // Invalid empty collection
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PlayerLineupIds)
            .WithErrorMessage("At least one player lineup identifier must be provided for initialization.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when any element inside the <see cref="InitializePresenceRequest.PlayerLineupIds"/> collection is an empty GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PlayerLineupIdsContainsEmptyGuid()
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            PlayerLineupIds: new List<Guid> { Guid.NewGuid(), Guid.Empty } // Contains an invalid empty GUID
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PlayerLineupIds)
            .WithErrorMessage("Player lineup identifiers cannot be empty GUIDs.");
    }
}