using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Clubs.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Clubs.Handlers;

/// <summary>
/// Orchestrates the creation of a club, enforcing the 'one club per user' rule.
/// </summary>
public class CreateClubCommandHandler(
    IClubRepository repository,
    ILogger<CreateClubCommandHandler> logger) : IRequestHandler<CreateClubCommand, Guid>
{
    private readonly IClubRepository _repository = repository;
    private readonly ILogger<CreateClubCommandHandler> _logger = logger;

    public async Task<Guid> Handle(CreateClubCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to create club '{ClubName}' for user {UserId}",
            command.Name, command.CreatorUserId);

        // 1. Business Rule Enforcement
        bool alreadyOwnsClub = await _repository.HasExistingClubOwnershipAsync(command.CreatorUserId, cancellationToken);

        if (alreadyOwnsClub)
        {
            _logger.LogWarning("User {UserId} already owns a club. Aborting creation.", command.CreatorUserId);
            throw new ConflictException($"User {command.CreatorUserId} is already an owner of a club.");
        }

        // 2. Prepare Entity
        var club = new Club
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            CityId = command.CityId,
            CreatedAt = DateTime.UtcNow
        };

        // 3. Atomic Execution via Repository
        var resultId = await _repository.CreateWithOwnershipAsync(club, command.CreatorUserId, cancellationToken);

        _logger.LogInformation("Club '{ClubName}' created successfully with ID {ClubId}", command.Name, resultId);

        return resultId;
    }
}