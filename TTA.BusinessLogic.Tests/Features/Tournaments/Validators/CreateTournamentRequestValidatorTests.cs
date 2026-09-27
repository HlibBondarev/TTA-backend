using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.BusinessLogic.Features.Tournaments.Validators;

namespace TTA.BusinessLogic.Tests.Features.Tournaments.Validators;

/// <summary>
/// Unit tests for <see cref="CreateTournamentRequestValidator"/>.
/// Verifies that tournament creation logic enforces non-past start dates and valid date ranges.
/// </summary>
public class CreateTournamentRequestValidatorTests
{
    private readonly CreateTournamentRequestValidator _validator = new();

    /// <summary>
    /// Verifies that the validator fails when the tournament name is empty or too long.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Should_Have_Error_When_Name_Is_Invalid(string? name)
    {
        var request = new CreateTournamentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), name!, DateTime.UtcNow, null);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    /// <summary>
    /// Verifies that creation fails if the start date is before today's date.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_StartDate_Is_In_Past()
    {
        // Testing with yesterday's date
        var request = new CreateTournamentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "New Tournament", DateTime.UtcNow.AddDays(-1), null);

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.StartDate)
              .WithErrorMessage("Tournament cannot start in the past.");
    }

    /// <summary>
    /// Verifies that the end date cannot be earlier than the start date.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_EndDate_Is_Before_StartDate()
    {
        var startDate = DateTime.UtcNow.AddDays(1);
        var endDate = DateTime.UtcNow; // Before start date
        var request = new CreateTournamentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "New Tournament", startDate, endDate);

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.EndDate)
              .WithErrorMessage("End date must be greater than or equal to the start date.");
    }

    /// <summary>
    /// Verifies that a valid creation request passes all rules.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Creation_Request_Is_Valid()
    {
        var request = new CreateTournamentRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Pro Open 2024",
            DateTime.UtcNow.Date,
            DateTime.UtcNow.AddDays(2));

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}