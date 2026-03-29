using MediatR;
using System.Diagnostics.CodeAnalysis;

namespace TTA.BusinessLogic.Features.Clubs.Commands;

/// <summary>
/// Command to create a club, including the creator's identity.
/// </summary>
/// <param name="Name">Club name.</param>
/// <param name="CityId">City ID.</param>
/// <param name="CreatorUserId">The ID of the user who will own the club (from Auth0).</param>
[ExcludeFromCodeCoverage]
public record CreateClubCommand(
    string Name,
    Guid CityId,
    string CreatorUserId) : IRequest<Guid>;