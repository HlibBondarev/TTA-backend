using MediatR;
using TTA.BusinessLogic.Features.TimeAnchors.DTOs;

namespace TTA.BusinessLogic.Features.TimeAnchors.Queries;

/// <summary>
/// Query to retrieve detailed information about a specific time anchor by its ID.
/// </summary>
/// <param name="Id">The unique identifier of the time anchor.</param>
public record GetTimeAnchorByIdQuery(Guid Id) : IRequest<TimeAnchorResponse>;