using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Validators;

namespace TTA.BusinessLogic.Tests.Features.Matches.Validators;

/// <summary>
/// Unit tests for <see cref="RecordMatchResultRequestValidator"/> to ensure 
/// match scores and weather data are within valid ranges.
/// </summary>
public class RecordMatchResultRequestValidatorTests
{
    private readonly RecordMatchResultRequestValidator _validator = new();

    /// <summary>
    /// Verifies that a valid result request (with or without temperature) is accepted.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 20.0)]
    [InlineData(5, 3, null)]
    public void Should_Not_Have_Error_When_Result_Is_Valid(int homeScore, int guestScore, double? temp)
    {
        // Arrange
        var request = new RecordMatchResultRequest(homeScore, guestScore, temp);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that negative scores are rejected.
    /// </summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -5)]
    public void Should_Have_Error_When_Scores_Are_Negative(int homeScore, int guestScore)
    {
        // Arrange
        var request = new RecordMatchResultRequest(homeScore, guestScore, 20.0);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        if (homeScore < 0) result.ShouldHaveValidationErrorFor(x => x.HomeScore);
        if (guestScore < 0) result.ShouldHaveValidationErrorFor(x => x.GuestScore);
    }

    /// <summary>
    /// Verifies that extreme temperature values outside the defined range trigger errors.
    /// </summary>
    [Theory]
    [InlineData(-51)]
    [InlineData(61)]
    public void Should_Have_Error_When_Temperature_Is_Out_Of_Range(double invalidTemp)
    {
        // Arrange
        var request = new RecordMatchResultRequest(1, 1, invalidTemp);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Temperature)
            .WithErrorMessage("Temperature must be between -50 and 60 degrees Celsius.");
    }
}