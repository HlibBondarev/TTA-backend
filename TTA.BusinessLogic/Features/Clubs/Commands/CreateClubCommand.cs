using MediatR;

namespace TTA.BusinessLogic.Features.Clubs.Commands;

/// <summary>
/// Domain command containing full data needed to create a club.
/// </summary>
public record CreateClubCommand(
    string Name,
    Guid CityId,
    string CreatorUserId) : IRequest<Guid>;