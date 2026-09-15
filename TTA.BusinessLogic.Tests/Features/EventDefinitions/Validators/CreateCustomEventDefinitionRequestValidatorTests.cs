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
            IsPositive: true
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
            IsPositive: true
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
            IsPositive: true
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
            IsPositive: true
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
            IsPositive: true
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
            IsPositive: true
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.ShortName)
            .WithErrorMessage("Short name must not exceed 10 characters.");
    }
}