using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Validators;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Tests.Features.Teams.Validators;

/// <summary>
/// Unit tests for <see cref="AddTeamMemberRequestValidator"/>.
/// </summary>
public class AddTeamMemberRequestValidatorTests
{
    private readonly AddTeamMemberRequestValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_UserEmail_Is_Valid()
    {
        // Arrange
        var request = new AddTeamMemberRequest("user_email@mail.com", TeamRole.Player, true);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_UserEmail_Is_Invalid()
    {
        // Arrange
        var request = new AddTeamMemberRequest("user_email", TeamRole.Player, true);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.UserEmail);
    }

    [Fact]
    public void Should_Have_Error_When_UserEmail_Is_Empty()
    {
        // Arrange
        var request = new AddTeamMemberRequest("", TeamRole.Player, true);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.UserEmail);
    }

    [Fact]
    public void Should_Have_Error_When_UserEmail_Is_Null()
    {
        // Arrange
        var request = new AddTeamMemberRequest(null!, TeamRole.HeadCoach, true);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.UserEmail);
    }

    [Fact]
    public void Should_Have_Error_When_RoleInTeam_Is_Invalid()
    {
        // Arrange
        var request = new AddTeamMemberRequest("auth0|123", (TeamRole)99, true);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.RoleInTeam);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new AddTeamMemberRequest("user_email@mail.com", TeamRole.AssistantCoach, false);

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }
}