using FluentValidation;
using TTA.BusinessLogic.Features.Matches.Queries;

namespace TTA.BusinessLogic.Features.Matches.Validators;

/// <summary>
/// Validator for <see cref="GetMatchAnalysisSnapshotQuery"/>.
/// Ensures that both the match and the tracked team identifiers are provided.
/// </summary>
public class GetMatchAnalysisSnapshotQueryValidator : AbstractValidator<GetMatchAnalysisSnapshotQuery>
{
    /// <summary>
    /// Initializes validation rules for match analysis snapshot queries.
    /// </summary>
    public GetMatchAnalysisSnapshotQueryValidator()
    {
        RuleFor(x => x.MatchId)
            .NotEmpty()
            .WithMessage("Match ID is required.");

        RuleFor(x => x.TeamId)
            .NotEmpty()
            .WithMessage("Team ID is required.");
    }
}
