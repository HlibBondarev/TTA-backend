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
            TimeIn: DateTime.UtcNow,
            PresenceItems: new List<PlayerPresenceItemDto>
            {
                new(Guid.NewGuid(), Guid.NewGuid()),
                new(Guid.NewGuid(), Guid.NewGuid())
            }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="InitializePresenceRequest.PeriodNumber"/> is zero or negative.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_Should_HaveError_When_PeriodNumberIsInvalid(int invalidPeriod)
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: invalidPeriod,
            TimeIn: DateTime.UtcNow,
            PresenceItems: new List<PlayerPresenceItemDto> { new(Guid.NewGuid(), Guid.NewGuid()) }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PeriodNumber)
            .WithErrorMessage("Period number must be greater than zero.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="InitializePresenceRequest.TimeIn"/> is empty or default.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_TimeInIsEmpty()
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            TimeIn: default,
            PresenceItems: new List<PlayerPresenceItemDto> { new(Guid.NewGuid(), Guid.NewGuid()) }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TimeIn)
            .WithErrorMessage("TimeIn timestamp must be specified.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when <see cref="InitializePresenceRequest.PresenceItems"/> collection is empty.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PresenceItemsIsEmpty()
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            PresenceItems: new List<PlayerPresenceItemDto>()
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PresenceItems)
            .WithErrorMessage("At least one player presence item must be provided for initialization.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when any element's Id inside <see cref="InitializePresenceRequest.PresenceItems"/> is an empty GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PresenceItemHasEmptyId()
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            PresenceItems: new List<PlayerPresenceItemDto> { new(Guid.Empty, Guid.NewGuid()) }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor("PresenceItems[0].Id")
            .WithErrorMessage("Presence identifier cannot be an empty GUID.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when any element's MatchLineupId inside <see cref="InitializePresenceRequest.PresenceItems"/> is an empty GUID.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PresenceItemHasEmptyMatchLineupId()
    {
        // Arrange
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            PresenceItems: new List<PlayerPresenceItemDto> { new(Guid.NewGuid(), Guid.Empty) }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor("PresenceItems[0].MatchLineupId")
            .WithErrorMessage("Player lineup identifier cannot be an empty GUID.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when MatchLineupIds in <see cref="InitializePresenceRequest.PresenceItems"/> contain duplicates.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_MatchLineupIdsAreNotUnique()
    {
        // Arrange
        var duplicateLineupId = Guid.NewGuid();
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            PresenceItems: new List<PlayerPresenceItemDto>
            {
                new(Guid.NewGuid(), duplicateLineupId),
                new(Guid.NewGuid(), duplicateLineupId)
            }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PresenceItems)
            .WithErrorMessage("Player lineup identifiers must be unique within the request.");
    }

    /// <summary>
    /// Verifies that a validation error is generated when Presence Ids in <see cref="InitializePresenceRequest.PresenceItems"/> contain duplicates.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveError_When_PresenceIdsAreNotUnique()
    {
        // Arrange
        var duplicatePresenceId = Guid.NewGuid();
        var request = new InitializePresenceRequest(
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            PresenceItems: new List<PlayerPresenceItemDto>
            {
                new(duplicatePresenceId, Guid.NewGuid()),
                new(duplicatePresenceId, Guid.NewGuid())
            }
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PresenceItems)
            .WithErrorMessage("Presence identifiers must be unique within the request.");
    }
}