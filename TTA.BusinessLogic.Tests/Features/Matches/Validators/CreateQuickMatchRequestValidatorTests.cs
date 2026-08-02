using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Validators;

namespace TTA.BusinessLogic.Tests.Features.Matches.Validators;

/// <summary>
/// Unit tests for <see cref="CreateQuickMatchRequestValidator"/>.
/// </summary>
public class CreateQuickMatchRequestValidatorTests
{
    private readonly CreateQuickMatchRequestValidator _validator = new();

    /// <summary>
    /// Verifies that validation fails when <see cref="CreateQuickMatchRequest.SportId"/> is an empty GUID.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_SportId_Is_Empty()
    {
        // Arrange
        var request = new CreateQuickMatchRequest
        {
            SportId = Guid.Empty,
            ConfigurationId = Guid.NewGuid()
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.SportId)
            .WithErrorMessage("SportId is required and cannot be an empty GUID.");
    }

    /// <summary>
    /// Verifies that validation passes when <see cref="CreateQuickMatchRequest.SportId"/> is a valid GUID and <see cref="CreateQuickMatchRequest.ConfigurationId"/> is omitted.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_SportId_Is_Valid_And_ConfigurationId_Is_Null()
    {
        // Arrange
        var request = new CreateQuickMatchRequest
        {
            SportId = Guid.NewGuid(),
            ConfigurationId = null
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveValidationErrorFor(x => x.SportId);
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that validation passes when both <see cref="CreateQuickMatchRequest.SportId"/> and <see cref="CreateQuickMatchRequest.ConfigurationId"/> are valid GUIDs.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Both_Ids_Are_Valid()
    {
        // Arrange
        var request = new CreateQuickMatchRequest
        {
            SportId = Guid.NewGuid(),
            ConfigurationId = Guid.NewGuid()
        };

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}