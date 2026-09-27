using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Validators;

namespace TTA.Tests.Unit.Features.Matches.Validators;

/// <summary>
/// Unit tests for <see cref="AddUserToTrackedMatchRequestValidator"/>.
/// </summary>
public class AddUserToTrackedMatchRequestValidatorTests
{
    private readonly AddUserToTrackedMatchRequestValidator _validator = new();

    /// <summary>
    /// Verifies that validation passes when a valid email address is provided.
    /// </summary>
    [Fact]
    public void Validate_ShouldNotHaveAnyErrors_WhenEmailIsValid()
    {
        // Arrange
        var request = new AddUserToTrackedMatchRequest("user@example.com");

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that validation fails when email is empty or contains only whitespace.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldHaveValidationError_WhenEmailIsEmpty(string emptyEmail)
    {
        // Arrange
        var request = new AddUserToTrackedMatchRequest(emptyEmail);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("User email is required.");
    }

    /// <summary>
    /// Verifies that validation fails when email format is invalid.
    /// </summary>
    [Theory]
    [InlineData("plainaddress")]
    [InlineData("email.domain.com")]
    [InlineData("@domain.com")]
    [InlineData("email@")]
    [InlineData("email@@domain.com")]
    [InlineData("email@domain@domain.com")]
    public void Validate_ShouldHaveValidationError_WhenEmailFormatIsInvalid(string invalidEmail)
    {
        // Arrange
        var request = new AddUserToTrackedMatchRequest(invalidEmail);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Email)
            .WithErrorMessage("A valid email address must be provided.");
    }
}