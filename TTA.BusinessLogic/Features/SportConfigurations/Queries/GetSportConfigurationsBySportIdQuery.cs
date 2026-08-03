using MediatR;
using TTA.BusinessLogic.Features.SportConfigurations.DTOs;

namespace TTA.BusinessLogic.Features.SportConfigurations.Queries;

/// <summary>
/// Query record for retrieving all configurations for a specific sport.
/// </summary>
/// <param name="SportId">The unique identifier of the target sport.</param>
public record GetSportConfigurationsBySportIdQuery(Guid SportId) : IRequest<IEnumerable<SportConfigurationResponse>>;