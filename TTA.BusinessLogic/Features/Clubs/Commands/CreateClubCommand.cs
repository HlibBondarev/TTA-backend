using MediatR;
using System.Diagnostics.CodeAnalysis;

namespace TTA.BusinessLogic.Features.Clubs.Commands;

/// <summary>
/// Domain command containing full data needed to create a club.
/// </summary>
[ExcludeFromCodeCoverage]
public record CreateClubCommand(
    string Name,
    Guid CityId,
    string CreatorUserId) : IRequest<Guid>;