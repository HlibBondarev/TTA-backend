using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.BusinessLogic.Features.Tournaments.Validators;

namespace TTA.BusinessLogic.Tests.Features.Tournaments.Validators;

/// <summary>
/// Unit tests for <see cref="UpdateTournamentRequestValidator"/>.
/// Verifies that updates adhere to the same date constraints as creation.
/// </summary>
public class UpdateTournamentRequestValidatorTests
{
    private readonly UpdateTournamentRequestValidator _validator = new();

    /// <summary>
    /// Verifies that update fails if required identifiers are empty.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Guids_Are_Empty()
    {
        var request = new UpdateTournamentRequest(Guid.Empty, Guid.Empty, Guid.Empty, "Update", DateTime.UtcNow, null);
        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.SportId);
        result.ShouldHaveValidationErrorFor(x => x.CityId);
        result.ShouldHaveValidationErrorFor(x => x.ConfigurationId);
    }

    /// <summary>
    /// Verifies that even during update, the start date cannot be shifted to the past.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Updating_StartDate_To_Past()
    {
        var request = new UpdateTournamentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Update", DateTime.UtcNow.AddDays(-2), null);

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.StartDate)
              .WithErrorMessage("Tournament cannot start in the past.");
    }

    /// <summary>
    /// Verifies that the date range integrity is checked during updates.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Update_EndDate_Is_Invalid()
    {
        var startDate = DateTime.UtcNow.AddDays(5);
        var endDate = DateTime.UtcNow.AddDays(4);
        var request = new UpdateTournamentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Update", startDate, endDate);

        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.EndDate);
    }

    /// <summary>
    /// Verifies that a valid update request passes all validation rules.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Update_Request_Is_Valid()
    {
        var request = new UpdateTournamentRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Updated Name",
            DateTime.UtcNow.AddHours(1),
            null);

        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}