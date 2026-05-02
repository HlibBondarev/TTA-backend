using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the update of an existing match lineup entry with database error handling.
/// </summary>
public class UpdatePlayerInMatchLineupHandler(
    IMatchLineupRepository matchLineupRepository,
    ILogger<UpdatePlayerInMatchLineupHandler> logger) : IRequestHandler<UpdatePlayerInMatchLineupCommand, Guid>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<UpdatePlayerInMatchLineupHandler> _logger = logger;

    /// <summary>
    /// Updates the lineup entry by fetching the existing record, applying changes, 
    /// and catching database-level validation errors.
    /// </summary>
    /// <param name="request">The update command containing modified lineup data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unique identifier of the updated record.</returns>
    /// <exception cref="NotFoundException">Thrown if the lineup entry or specified position does not exist.</exception>
    /// <exception cref="ConflictException">Thrown if database-level business rules are violated.</exception>
    public async Task<Guid> Handle(UpdatePlayerInMatchLineupCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to update match lineup entry {Id}.", request.Id);

        // 1. Retrieve the existing entity from the database
        var existingModel = await _matchLineupRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existingModel == null)
        {
            _logger.LogWarning("Update failed: Match lineup entry {Id} not found.", request.Id);
            throw new NotFoundException($"Match lineup entry with ID {request.Id} was not found.");
        }

        // 2. Apply command changes to the model via extension method
        var updatedModel = request.SetToModel(existingModel);

        try
        {
            // 3. Persist updated entity
            await _matchLineupRepository.UpsertLineupItemAsync(updatedModel, cancellationToken);

            _logger.LogInformation("Match lineup entry {Id} successfully updated.", request.Id);
            return request.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign Key Violation
        {
            _logger.LogWarning(ex, "Update failed: Position ID {PositionId} does not exist.", request.PositionId);
            throw new NotFoundException("The specified position does not exist.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
        {
            _logger.LogWarning(ex, "Update failed: Unique constraint violation for lineup entry {Id}.", request.Id);
            throw new ConflictException("This player is already registered in the lineup for this match.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001") // Custom error: Ineligible player
        {
            _logger.LogWarning(ex, "Lineup update failed: Player {PlayerRosterId} does not belong to match teams.", existingModel.PlayerRosterId);
            throw new ConflictException("The player does not belong to any team participating in this match.", ex);
        }
    }
}