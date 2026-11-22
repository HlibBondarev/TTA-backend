using TTA.DataAccess.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.EventDefinitions.Validators;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Validators;

/// <summary>
/// Unit tests for <see cref="CreateCustomEventDefinitionRequestValidator"/> ensuring 
/// client request parameters meet length and presence constraints.
/// </summary>
public class CreateCustomEventDefinitionRequestValidatorTests
{
    private readonly CreateCustomEventDefinitionRequestValidator _validator = new();

    /// <summary>
    /// Verifies that a valid creation request passes all validation rules.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: "Custom Timeout",
            ShortName: "CTO",
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an error is triggered when the definition ID is empty.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Id_Is_Empty()
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.Empty,
            Name: "Custom Timeout",
            ShortName: "CTO",
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Definition ID is required.");
    }

    /// <summary>
    /// Verifies that an error is triggered when the name is empty or null.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Should_Have_Error_When_Name_Is_Empty(string? invalidName)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: invalidName!,
            ShortName: "CTO",
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name is required.");
    }

    /// <summary>
    /// Verifies that an error is triggered when the name exceeds the maximum length of 50 characters.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_Name_Exceeds_Maximum_Length()
    {
        // Arrange
        var longName = new string('A', 51);
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: longName,
            ShortName: "CTO",
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Name)
            .WithErrorMessage("Name must not exceed 50 characters.");
    }

    /// <summary>
    /// Verifies that an error is triggered when the short name is empty or null.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Should_Have_Error_When_ShortName_Is_Empty(string? invalidShortName)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: "Custom Timeout",
            ShortName: invalidShortName!,
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ShortName)
            .WithErrorMessage("Short name is required.");
    }

    /// <summary>
    /// Verifies that an error is triggered when the short name exceeds the maximum length of 10 characters.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_ShortName_Exceeds_Maximum_Length()
    {
        // Arrange
        var longShortName = new string('A', 11);
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: "Custom Timeout",
            ShortName: longShortName,
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ShortName)
            .WithErrorMessage("Short name must not exceed 10 characters.");
    }

    /// <summary>
    /// Verifies that no validation error is triggered when the name is exactly at the maximum allowed length of 50 characters.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Name_Is_At_Maximum_Length()
    {
        // Arrange
        var maxName = new string('A', 50);
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: maxName,
            ShortName: "CTO",
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Name);
    }

    /// <summary>
    /// Verifies that no validation error is triggered when the short name is exactly at the maximum allowed length of 10 characters.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_ShortName_Is_At_Maximum_Length()
    {
        // Arrange
        var maxShortName = new string('A', 10);
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: "Custom Timeout",
            ShortName: maxShortName,
            IsPositive: true,
            IsShot: false
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.ShortName);
    }

    /// <summary>
    /// Verifies that an error is triggered when the score is negative.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Should_Have_Error_When_Score_Is_Negative(int invalidScore)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: "Custom Goal",
            ShortName: "CG",
            IsPositive: true,
            IsShot: true,
            Score: invalidScore
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Score)
            .WithErrorMessage("Score must be non-negative.");
    }

    /// <summary>
    /// Verifies that no validation error is triggered when the score is zero or positive.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Should_Not_Have_Error_When_Score_Is_NonNegative(int validScore)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(),
            Name: "Custom Goal",
            ShortName: "CG",
            IsPositive: true,
            IsShot: true,
            Score: validScore
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.Score);
    }

    /// <summary>
    /// Verifies that every defined <see cref="FoulImpact"/> value passes validation.
    /// </summary>
    [Theory]
    [InlineData(FoulImpact.None)]
    [InlineData(FoulImpact.PersonalFoul)]
    [InlineData(FoulImpact.DisqualificationWithSub)]
    [InlineData(FoulImpact.DisqualificationNoSub)]
    public void Should_Not_Have_Error_When_FoulImpact_Is_Defined(FoulImpact foulImpact)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(), Name: "Custom Foul", ShortName: "CF", IsPositive: false, FoulImpact: foulImpact);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.FoulImpact);
    }

    /// <summary>
    /// Verifies that an undefined <see cref="FoulImpact"/> value is rejected by the <c>IsInEnum</c> rule.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void Should_Have_Error_When_FoulImpact_Is_Not_Defined(int rawValue)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(), Name: "Custom Foul", ShortName: "CF", IsPositive: false, FoulImpact: (FoulImpact)rawValue);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.FoulImpact);
    }

    /// <summary>
    /// Verifies that a missing or positive penalty duration passes validation.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(18)]
    [InlineData(300)]
    public void Should_Not_Have_Error_When_PenaltyDurationSeconds_Is_Null_Or_Positive(int? seconds)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(), Name: "Custom Foul", ShortName: "CF", IsPositive: false,
            FoulImpact: FoulImpact.DisqualificationWithSub, PenaltyDurationSeconds: seconds);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.PenaltyDurationSeconds);
    }

    /// <summary>
    /// Verifies that a zero or negative penalty duration is rejected.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-300)]
    public void Should_Have_Error_When_PenaltyDurationSeconds_Is_Not_Positive(int seconds)
    {
        // Arrange
        var request = new CreateCustomEventDefinitionRequest(
            Id: Guid.NewGuid(), Name: "Custom Foul", ShortName: "CF", IsPositive: false,
            FoulImpact: FoulImpact.DisqualificationNoSub, PenaltyDurationSeconds: seconds);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.PenaltyDurationSeconds)
            .WithErrorMessage("Penalty duration must be greater than zero.");
    }

    /// <summary>
    /// Verifies that omitted foul impact fields default to <see cref="FoulImpact.None"/> and no penalty duration.
    /// </summary>
    [Fact]
    public void Request_ShouldDefaultFoulImpactToNone_WhenNotSpecified()
    {
        // Arrange & Act
        var request = new CreateCustomEventDefinitionRequest(Guid.NewGuid(), "Custom", "C", true);

        // Assert
        request.FoulImpact.Should().Be(FoulImpact.None);
        request.PenaltyDurationSeconds.Should().BeNull();
    }
}
