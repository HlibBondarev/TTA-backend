using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Clubs.DTOs;
using TTA.BusinessLogic.Features.Clubs.Validators;

namespace TTA.BusinessLogic.Tests.Features.Clubs.Validators;

public class CreateClubRequestValidatorTests
{
    private readonly CreateClubRequestValidator validator;

    public CreateClubRequestValidatorTests()
    {
        validator = new CreateClubRequestValidator();
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Empty()
    {
        // Arrange
        var request = new CreateClubRequest(string.Empty, Guid.NewGuid());

        // Act & Assert
        var result = validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name)
              .WithErrorMessage("Club name is required.");
    }

    [Fact]
    public void Should_Have_Error_When_Name_Is_Too_Short()
    {
        // Arrange
        var request = new CreateClubRequest("Ab", Guid.NewGuid());

        // Act & Assert
        var result = validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name)
              .WithErrorMessage("Name must be at least 3 characters long."); // Added "long" to match the validator
    }

    [Fact]
    public void Should_Have_Error_When_Name_Exceeds_Max_Length()
    {
        // Arrange
        var longName = new string('a', 101);
        var request = new CreateClubRequest(longName, Guid.NewGuid());

        // Act & Assert
        var result = validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Name)
              .WithErrorMessage("Name must not exceed 100 characters.");
    }

    [Fact]
    public void Should_Have_Error_When_CityId_Is_Empty()
    {
        // Arrange
        var request = new CreateClubRequest("Valid Club Name", Guid.Empty);

        // Act & Assert
        var result = validator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.CityId)
              .WithErrorMessage("City must be selected.");
    }

    [Fact]
    public void Should_Not_Have_Error_When_Request_Is_Valid()
    {
        // Arrange
        var request = new CreateClubRequest("Standard Club", Guid.NewGuid());

        // Act & Assert
        var result = validator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }
}