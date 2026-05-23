using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Validators;

namespace TTA.BusinessLogic.Tests.Features.PlayerPresences.Validators;

/// <summary>
/// Unit tests for the <see cref="SubstitutePlayerRequestValidator"/> class.
/// Validates business rules for incoming player substitution requests.
/// </summary>
public class SubstitutePlayerRequestValidatorTests
{
    private readonly SubstitutePlayerRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubstitutePlayerRequestValidatorTests"/> class.
    /// </summary>
    public SubstitutePlayerRequestValidatorTests()
    {
        _validator = new SubstitutePlayerRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator does not return any errors for a perfectly valid request payload.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_RequestIsValid()
    {
        // Arrange
        var request = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: Guid.NewGuid(),
            PlayerInLineupId: Guid.NewGuid()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="SubstitutePlayerRequest.PeriodNumber"/> is zero or negative.
    /// </summary>
    /// <param name="invalidPeriod">The invalid period sequence number supplied by the InlineData attribute.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new SubstitutePlayerRequest(
            PeriodNumber: invalidPeriod,
            PlayerOutLineupId: Guid.NewGuid(),
            PlayerInLineupId: Guid.NewGuid()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="SubstitutePlayerRequest.PlayerOutLineupId"/> is an empty globally unique identifier.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PlayerOutLineupIdIsEmpty()
    {
        // Arrange
        var request = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: Guid.Empty, // Invalid empty GUID
            PlayerInLineupId: Guid.NewGuid()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PlayerOutLineupId)
            .WithErrorMessage("Outgoing player lineup identifier is required.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="SubstitutePlayerRequest.PlayerInLineupId"/> is an empty globally unique identifier.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PlayerInLineupIdIsEmpty()
    {
        // Arrange
        var request = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: Guid.NewGuid(),
            PlayerInLineupId: Guid.Empty // Invalid empty GUID
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PlayerInLineupId)
            .WithErrorMessage("Incoming player lineup identifier is required.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when the incoming and outgoing player identifiers are exactly identical.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_IncomingAndOutgoingPlayersAreTheSame()
    {
        // Arrange
        var samePlayerId = Guid.NewGuid();

        var request = new SubstitutePlayerRequest(
            PeriodNumber: 1,
            PlayerOutLineupId: samePlayerId,
            PlayerInLineupId: samePlayerId // Attempting to substitute a player with themselves
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PlayerInLineupId)
            .WithErrorMessage("Incoming and outgoing players must be different.");
    }
}