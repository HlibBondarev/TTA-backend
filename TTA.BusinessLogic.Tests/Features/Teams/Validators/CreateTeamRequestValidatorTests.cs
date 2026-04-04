using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Teams.DTOs;
using TTA.BusinessLogic.Features.Teams.Validators;
using TTA.DataAccess.Enums;

namespace TTA.BusinessLogic.Tests.Features.Teams.Validators;

/// <summary>
/// Unit tests for <see cref="CreateTeamRequestValidator"/>.
/// </summary>
public class CreateTeamRequestValidatorTests
{
    private readonly CreateTeamRequestValidator _validator = new();

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        var request = new CreateTeamRequest("", Guid.NewGuid(), 2010, Gender.Male);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Too_Short()
    {
        var request = new CreateTeamRequest("Ab", Guid.NewGuid(), 2010, Gender.Male);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Should_Have_Error_When_MinBirthYear_Is_In_Future()
    {
        var futureYear = DateTime.UtcNow.Year + 1;
        var request = new CreateTeamRequest("U-10", Guid.NewGuid(), futureYear, Gender.Male);
        var result = _validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.MinBirthYear);
    }

    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        var request = new CreateTeamRequest("Standard Team", Guid.NewGuid(), 2012, Gender.Female);
        var result = _validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}