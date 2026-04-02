using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Players.DTOs;
using TTA.BusinessLogic.Features.Players.Validators;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Tests.Features.Players.Validators;

/// <summary>
/// Contains unit tests for the <see cref="CreatePlayerRequestValidator"/>.
/// </summary>
public class CreatePlayerRequestValidatorTests
{
    private readonly CreatePlayerRequestValidator _validator = new();
    private readonly DateOnly _today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// Verifies that a valid request passes validation without errors.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new CreatePlayerRequest(
            FirstName: "John",
            LastName: "Doe",
            BirthDate: _today.AddYears(-20),
            Gender: Gender.Male
        );

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Checks validation for mandatory fields.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Should_Have_Error_When_Names_Are_Empty(string? invalidName)
    {
        // Arrange
        var request = new CreatePlayerRequest(
            invalidName!,
            invalidName!,
            _today.AddYears(-20),
            Gender.Male);

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }

    /// <summary>
    /// Ensures that names exceeding the maximum length are rejected.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_FirstName_Exceeds_MaxLength()
    {
        // Arrange
        var longName = new string('A', 101);
        var request = new CreatePlayerRequest(
            longName,
            "Doe",
            _today.AddYears(-20),
            Gender.Male);

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.FirstName)
              .WithErrorMessage("First name cannot exceed 100 characters.");
    }

    /// <summary>
    /// Validates the business rule for minimum and maximum age.
    /// </summary>
    [Theory]
    [InlineData(2)]   // Too young
    [InlineData(105)] // Too old
    public void Should_Have_Error_When_Age_Is_Outside_Reasonable_Range(int age)
    {
        // Arrange
        var request = new CreatePlayerRequest(
            "John",
            "Doe",
            _today.AddYears(-age),
            Gender.Male);

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.BirthDate)
              .WithErrorMessage("Player age must be between 5 and 100 years.");
    }

    /// <summary>
    /// Verifies that an undefined Gender value triggers a validation error.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Gender_Is_Invalid()
    {
        // Arrange
        var request = new CreatePlayerRequest(
            "John",
            "Doe",
            _today.AddYears(-20),
            (Gender)99); // Undefined enum value

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Gender);
    }

    /// <summary>
    /// Ensures that BirthDate cannot be in the future.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_BirthDate_Is_In_Future()
    {
        // Arrange
        var request = new CreatePlayerRequest(
            "John",
            "Doe",
            _today.AddDays(1),
            Gender.Male);

        // Act & Assert
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.BirthDate);
    }
}