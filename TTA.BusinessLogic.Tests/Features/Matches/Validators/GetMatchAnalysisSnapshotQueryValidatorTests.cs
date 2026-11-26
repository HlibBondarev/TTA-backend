using FluentValidation.TestHelper;
using TTA.BusinessLogic.Features.Matches.Queries;
using TTA.BusinessLogic.Features.Matches.Validators;

namespace TTA.Tests.Unit.Features.Matches.Validators;

/// <summary>
/// Unit tests for <see cref="GetMatchAnalysisSnapshotQueryValidator"/>.
/// </summary>
public class GetMatchAnalysisSnapshotQueryValidatorTests
{
    private readonly GetMatchAnalysisSnapshotQueryValidator _validator = new();

    /// <summary>
    /// Verifies that validation passes when both identifiers are provided.
    /// </summary>
    [Fact]
    public void Validate_ShouldNotHaveAnyErrors_WhenIdentifiersAreProvided()
    {
        // Arrange
        var query = new GetMatchAnalysisSnapshotQuery(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// Verifies that validation fails when the match identifier is empty.
    /// </summary>
    [Fact]
    public void Validate_ShouldHaveValidationError_WhenMatchIdIsEmpty()
    {
        // Arrange
        var query = new GetMatchAnalysisSnapshotQuery(Guid.Empty, Guid.NewGuid());

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.MatchId).WithErrorMessage("Match ID is required.");
        result.ShouldNotHaveValidationErrorFor(x => x.TeamId);
    }

    /// <summary>
    /// Verifies that validation fails when the tracked team identifier is empty.
    /// </summary>
    [Fact]
    public void Validate_ShouldHaveValidationError_WhenTeamIdIsEmpty()
    {
        // Arrange
        var query = new GetMatchAnalysisSnapshotQuery(Guid.NewGuid(), Guid.Empty);

        // Act
        var result = _validator.TestValidate(query);

        // Assert
        result.ShouldHaveValidationErrorFor(x => x.TeamId).WithErrorMessage("Team ID is required.");
        result.ShouldNotHaveValidationErrorFor(x => x.MatchId);
    }
}
