using FluentValidation;
using TTA.BusinessLogic.Features.GameEvents.Validators;
using TTA.BusinessLogic.Features.Matches.DTOs;
using TTA.BusinessLogic.Features.PlayerPresences.Validators;
using TTA.BusinessLogic.Features.TimeAnchors.Validators;

namespace TTA.BusinessLogic.Features.Matches.Validators;

/// <summary>
/// Validator for the <see cref="MatchSyncBatchRequest"/> payload.
/// Reuses individual item validators for nested collections.
/// </summary>
public class MatchSyncBatchRequestValidator : AbstractValidator<MatchSyncBatchRequest>
{
    /// <summary>
    /// Initializes validation rules for <see cref="MatchSyncBatchRequest"/>.
    /// </summary>
    public MatchSyncBatchRequestValidator()
    {
        RuleFor(x => x.Events)
            .NotNull().WithMessage("Events collection cannot be null.");

        RuleFor(x => x.Anchors)
            .NotNull().WithMessage("Anchors collection cannot be null.");

        RuleFor(x => x.Presences)
            .NotNull().WithMessage("Presences collection cannot be null.");

        RuleForEach(x => x.Events)
            .SetValidator(new CreateGameEventRequestValidator());

        RuleForEach(x => x.Anchors)
            .SetValidator(new CreateTimeAnchorRequestValidator());

        RuleForEach(x => x.Presences)
            .SetValidator(new CreatePlayerPresenceRequestValidator());
    }
}