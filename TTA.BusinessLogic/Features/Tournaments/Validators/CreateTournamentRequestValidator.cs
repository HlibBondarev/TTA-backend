using FluentValidation;
using TTA.BusinessLogic.Features.Tournaments.DTOs;

namespace TTA.BusinessLogic.Features.Tournaments.Validators;

/// <summary>
/// Validator for the tournament creation request.
/// </summary>
public class CreateTournamentRequestValidator : AbstractValidator<CreateTournamentRequest>
{
    public CreateTournamentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SportId).NotEmpty();
        RuleFor(x => x.ConfigurationId).NotEmpty();
        RuleFor(x => x.CityId).NotEmpty();
        RuleFor(x => x.StartDate)
            .NotEmpty()
            .Must(date => date.Date >= DateTime.UtcNow.Date)
            .WithMessage("Tournament cannot start in the past.");
        RuleFor(x => x.EndDate)
            .Must((req, endDate) => !endDate.HasValue || endDate.Value >= req.StartDate)
            .WithMessage("End date must be greater than or equal to the start date.");
    }
}
