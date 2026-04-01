using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Players.Commands;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Players.Handlers;

/// <summary>
/// Orchestrates the process of adding a new player to the club's database.
/// </summary>
/// <param name="repository">The player repository.</param>
/// <param name="logger">The logger instance.</param>
public class CreatePlayerHandler(
    IPlayerRepository repository,
    ILogger<CreatePlayerHandler> logger) : IRequestHandler<CreatePlayerCommand, Guid>
{
    private readonly IPlayerRepository _repository = repository;
    private readonly ILogger<CreatePlayerHandler> _logger = logger;

    /// <summary>
    /// Handles the player creation process.
    /// </summary>
    /// <param name="command">The command containing player details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The unique identifier of the newly created player.</returns>
    public async Task<Guid> Handle(CreatePlayerCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to create player {FirstName} {LastName} for club {ClubId}",
            command.FirstName, command.LastName, command.HomeClubId);

        // 1. Prepare Entity using extension method
        var player = command.ToModel();

        // 2. Persist to database via Repository
        var result = await _repository.CreatePlayerAsync(player, cancellationToken);

        _logger.LogInformation("Player '{FirstName} {LastName}' created successfully with ID {PlayerId}",
            player.FirstName, player.LastName, result.Id);

        return result.Id;
    }
}