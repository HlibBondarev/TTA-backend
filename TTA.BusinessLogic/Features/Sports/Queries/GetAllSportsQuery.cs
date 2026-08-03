using MediatR;
using TTA.BusinessLogic.Features.Sports.DTOs;

namespace TTA.BusinessLogic.Features.Sports.Queries;

/// <summary>
/// Query record for retrieving all available sports.
/// </summary>
public record GetAllSportsQuery : IRequest<IEnumerable<SportResponse>>;