using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.GameEvents.DTOs;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Validators;
using TTA.BusinessLogic.Features.PlayerPresences.DTOs;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Tests.Features.Matches.Validators;

/// <summary>
/// Unit tests for the <see cref="MatchSyncBatchRequestValidator"/> class.
/// </summary>
public class MatchSyncBatchRequestValidatorTests
{
    private readonly MatchSyncBatchRequestValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchSyncBatchRequestValidatorTests"/> class.
    /// </summary>
    public MatchSyncBatchRequestValidatorTests()
    {
        _validator = new MatchSyncBatchRequestValidator();
    }

    /// <summary>
    /// Verifies that the validator passes when all collections contain valid records.
    /// </summary>
    [Fact]
    public void Validator_Should_NotHaveErrors_When_BatchRequestIsValid()
    {
        // Arrange
        var validEvent = new CreateGameEventRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false);

        var validAnchor = new CreateTimeAnchorRequest(
            Id: Guid.NewGuid(),
            PeriodNumber: 1,
            Type: TimeAnchorType.PeriodStart,
            Timestamp: DateTime.UtcNow);

        var validPresence = new CreatePlayerPresenceRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.NewGuid(),
            PeriodNumber: 1,
            TimeIn: DateTime.UtcNow,
            TimeOut: null);

        var request = new MatchSyncBatchRequest(
            Events: [validEvent],
            Anchors: [validAnchor],
            Presences: [validPresence]);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that the validator returns errors when nested collections contain invalid elements.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveErrors_When_NestedItemsAreInvalid()
    {
        // Arrange
        var invalidEvent = new CreateGameEventRequest(
            Id: Guid.Empty,
            MatchLineupId: Guid.NewGuid(),
            EventDefinitionId: Guid.NewGuid(),
            PeriodNumber: 1,
            EventTimestamp: DateTime.UtcNow,
            IsLeadToGoal: false);

        var invalidPresence = new CreatePlayerPresenceRequest(
            Id: Guid.NewGuid(),
            MatchLineupId: Guid.Empty,
            PeriodNumber: 0,
            TimeIn: DateTime.UtcNow,
            TimeOut: null);

        var request = new MatchSyncBatchRequest(
            Events: [invalidEvent],
            Anchors: [],
            Presences: [invalidPresence]);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor("Events[0].Id");
        result.ShouldHaveValidationErrorFor("Presences[0].MatchLineupId");
        result.ShouldHaveValidationErrorFor("Presences[0].PeriodNumber");
    }

    /// <summary>
    /// Verifies that the validator returns errors when collections contain null items.
    /// </summary>
    [Fact]
    public void Validator_Should_HaveErrors_When_CollectionsContainNullItems()
    {
        // Arrange
        var request = new MatchSyncBatchRequest(
            Events: [null!],
            Anchors: [null!],
            Presences: [null!]);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor("Events[0]");
        result.ShouldHaveValidationErrorFor("Anchors[0]");
        result.ShouldHaveValidationErrorFor("Presences[0]");
    }
}