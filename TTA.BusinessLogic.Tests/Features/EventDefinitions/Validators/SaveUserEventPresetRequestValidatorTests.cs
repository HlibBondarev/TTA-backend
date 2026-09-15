using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.EventDefinitions.DTOs;
using TTA.BusinessLogic.Features.EventDefinitions.Validators;

namespace TTA.BusinessLogic.Tests.Features.EventDefinitions.Validators;

/// <summary>
/// Unit tests for <see cref="SaveUserEventPresetRequestValidator"/> verifying 
/// preset layout payload constraints.
/// </summary>
public class SaveUserEventPresetRequestValidatorTests
{
    private readonly SaveUserEventPresetRequestValidator _validator = new();

    /// <summary>
    /// Verifies that a valid request containing definition IDs passes validation.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new SaveUserEventPresetRequest(new[] { Guid.NewGuid(), Guid.NewGuid() });

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that a request with an empty collection of IDs passes validation.
    /// </summary>
    [Fact]
    public void Should_Not_Have_Error_When_Collection_Is_Empty()
    {
        // Arrange
        var request = new SaveUserEventPresetRequest(Enumerable.Empty<Guid>());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that an error is triggered when the event definition IDs collection is null.
    /// </summary>
    [Fact]
    public void Should_Have_Error_When_EventDefinitionIds_Is_Null()
    {
        // Arrange
        var request = new SaveUserEventPresetRequest(null!);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.EventDefinitionIds)
            .WithErrorMessage("Event definition IDs collection cannot be null.");
    }
}