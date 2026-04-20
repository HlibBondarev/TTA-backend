using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Validators;

namespace TTA.BusinessLogic.Tests.Features.Matches.Validators;

/// <summary>
/// Unit tests for <see cref="ScheduleMatchRequestValidator"/> ensuring that match scheduling 
/// requests adhere to business rules regarding teams and dates.
/// </summary>
public class ScheduleMatchRequestValidatorTests
{
    private readonly ScheduleMatchRequestValidator _validator = new();

    /// <summary>
    /// Verifies that a valid schedule request passes all validation rules.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new ScheduleMatchRequest(
            HomeTeamId: Guid.NewGuid(),
            GuestTeamId: Guid.NewGuid(),
            ScheduledAt: DateTime.UtcNow.AddDays(1),
            MatchNumber: "MATCH-001",
            Venue: "Main Stadium"
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an error is triggered when the home team and guest team are the same.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Teams_Are_The_Same()
    {
        // Arrange
        var teamId = Guid.NewGuid();
        var request = new ScheduleMatchRequest(teamId, teamId, DateTime.UtcNow.AddDays(1), "M1", "Venue");

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.GuestTeamId)
            .WithErrorMessage("The guest team must be different from the home team.");
    }

    /// <summary>
    /// Verifies that scheduling a match in the past triggers a validation error.
    /// Updated to match the new error message from the validator.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_ScheduledAt_Is_In_Past()
    {
        // Arrange
        var request = new ScheduleMatchRequest(
            HomeTeamId: Guid.NewGuid(),
            GuestTeamId: Guid.NewGuid(),
            ScheduledAt: DateTime.UtcNow.AddHours(-1),
            MatchNumber: "M1",
            Venue: "Venue"
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        // Fixed: The error message must match exactly what is defined in the validator
        result.ShouldHaveValidationErrorFor(x => x.ScheduledAt)
            .WithErrorMessage("Match must be scheduled in the future.");
    }

    /// <summary>
    /// Verifies that excessively long strings for MatchNumber or Venue trigger errors.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Strings_Exceed_Limits()
    {
        // Arrange
        var request = new ScheduleMatchRequest(
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(1),
            new string('A', 51), // Limit is 50
            new string('B', 201) // Limit is 200
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MatchNumber);
        result.ShouldHaveValidationErrorFor(x => x.Venue);
    }
}