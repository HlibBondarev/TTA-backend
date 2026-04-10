using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.Validators;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Tests.Features.Teams.Validators;

/// <summary>
/// Unit tests for <see cref="TerminateMembershipCommandValidator"/>.
/// </summary>
public class TerminateMembershipCommandValidatorTests
{
    private readonly TerminateMembershipCommandValidator _validator = new();

    [Fact]
    public void Should_Not_Have_Error_When_Command_Is_Valid()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "user@example.com", TeamRole.Player, DateTime.UtcNow.AddHours(1));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Have_Error_When_UserEmail_Is_Invalid()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "invalid-email", TeamRole.Player, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.UserEmail);
    }

    [Fact]
    public void Should_Have_Error_When_TeamId_Is_Empty()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.Empty, "user@example.com", TeamRole.Player, null);

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TeamId);
    }

    [Fact]
    public void Should_Have_Error_When_LeftAt_Is_In_Past()
    {
        // Arrange
        var command = new TerminateMembershipCommand(Guid.NewGuid(), "user@example.com", TeamRole.Player, DateTime.UtcNow.AddDays(-1));

        // Act
        var result = _validator.TestValidate(command);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.LeftAt)
            .WithErrorMessage("Termination date cannot be in the past.");
    }
}