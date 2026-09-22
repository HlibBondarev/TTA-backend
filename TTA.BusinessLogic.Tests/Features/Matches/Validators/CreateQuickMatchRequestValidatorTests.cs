using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.Matches.Validators;

namespace TTA.BusinessLogic.Tests.Features.Matches.Validators;

/// < summary >
/// Unit tests for < see cref="CreateQuickMatchRequestValidator" / >.
/// < /summary >
public class CreateQuickMatchRequestValidatorTests
{
    private readonly CreateQuickMatchRequestValidator _validator = new();

    /// < summary >
    /// Verifies that validation fails when < see cref="CreateQuickMatchRequest.Id" / > is an empty GUID.
    /// < /summary >
    [Fact]
    public void Should_Have_Error_When_Id_Is_Empty()
    {
        // Arrange
        var request = new CreateQuickMatchRequest(Guid.Empty, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.Id)
            .WithErrorMessage("Match Id is required and cannot be an empty GUID.");
    }

    /// < summary >
    /// Verifies that validation fails when < see cref="CreateQuickMatchRequest.SportId" / > is an empty GUID.
    /// < /summary >
    [Fact]
    public void Should_Have_Error_When_SportId_Is_Empty()
    {
        // Arrange
        var request = new CreateQuickMatchRequest(Guid.NewGuid(), Guid.Empty);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.SportId)
            .WithErrorMessage("SportId is required and cannot be an empty GUID.");
    }

    /// < summary >
    /// Verifies that validation fails when < see cref="CreateQuickMatchRequest.TrackedTeamId" / > is specified as an empty GUID.
    /// < /summary >
    [Fact]
    public void Should_Have_Error_When_TrackedTeamId_Is_Empty_GUID()
    {
        // Arrange
        var request = new CreateQuickMatchRequest(Guid.NewGuid(), Guid.NewGuid(), TrackedTeamId: Guid.Empty);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TrackedTeamId!.Value)
            .WithErrorMessage("TrackedTeamId cannot be an empty GUID when specified.");
    }

    /// < summary >
    /// Verifies that validation passes when < see cref="CreateQuickMatchRequest.Id" / > and < see cref="CreateQuickMatchRequest.SportId" / > are valid GUIDs, while optional fields are omitted.
    /// < /summary >
    [Fact]
    public void Should_Not_Have_Error_When_Required_Ids_Are_Valid_And_Optionals_Are_Null()
    {
        // Arrange
        var request = new CreateQuickMatchRequest(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// < summary >
    /// Verifies that validation passes when all required and optional payload identifiers are valid GUIDs.
    /// < /summary >
    [Fact]
    public void Should_Not_Have_Error_When_All_Provided_Ids_Are_Valid()
    {
        // Arrange
        var request = new CreateQuickMatchRequest(
            Id: Guid.NewGuid(),
            SportId: Guid.NewGuid(),
            ConfigurationId: Guid.NewGuid(),
            TrackedTeamId: Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}