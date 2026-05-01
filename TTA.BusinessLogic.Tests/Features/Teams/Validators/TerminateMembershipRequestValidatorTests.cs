using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Validators;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Tests.Features.Teams.Validators;

/// <summary>
/// Unit tests for <see cref="TerminateMembershipRequestValidator"/>.
/// </summary>
public class TerminateMembershipRequestValidatorTests
{
    private readonly TerminateMembershipRequestValidator _validator = new();

    [Fact]
    public void Should_Not_Have_Error_When_Command_Is_Valid()
    {
        // Arrange
        var request = new TerminateMembershipRequest
        (
            UserEmail: "user@example.com",
            RoleInTeam: TeamRole.Player,
            LeftAt: DateTime.UtcNow.AddHours(1)
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_UserEmail_Is_Invalid()
    {
        // Arrange
        var request = new TerminateMembershipRequest
        (
            UserEmail: "invalid-email",
            RoleInTeam: TeamRole.Player,
            LeftAt: null
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.UserEmail);
    }

    [Fact]
    public void Should_Have_Error_When_LeftAt_Is_In_Past()
    {
        // Arrange
        var request = new TerminateMembershipRequest
        (
            UserEmail: "user@example.com",
            RoleInTeam: TeamRole.Player,
            LeftAt: DateTime.UtcNow.AddDays(-1)
        );

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.LeftAt)
            .WithErrorMessage("Termination date cannot be in the past.");
    }
}