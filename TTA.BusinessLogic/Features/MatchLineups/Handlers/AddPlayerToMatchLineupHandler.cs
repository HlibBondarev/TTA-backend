using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.MatchLineups.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.MatchLineups.Handlers;

/// <summary>
/// Handles the creation of a new match lineup entry with database error handling.
/// </summary>
public class AddPlayerToMatchLineupHandler(
    IMatchLineupRepository matchLineupRepository,
    ILogger<AddPlayerToMatchLineupHandler> logger) : IRequestHandler<AddPlayerToMatchLineupCommand, Guid>
{
    private readonly IMatchLineupRepository _matchLineupRepository = matchLineupRepository;
    private readonly ILogger<AddPlayerToMatchLineupHandler> _logger = logger;

    /// <summary>
    /// Executes the creation command, maps the command to a domain model, 
    /// and handles database-specific exceptions by mapping them to business exceptions.
    /// </summary>
    /// <param name="request">The creation command containing lineup details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unique identifier of the newly created record.</returns>
    /// <exception cref="ConflictException">Thrown when lineup limits are exceeded, the player is ineligible, or already exists.</exception>
    public async Task<Guid> Handle(AddPlayerToMatchLineupCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Attempting to create lineup entry for PlayerRoster {PlayerRosterId} in Match {MatchId}.",
            request.PlayerRosterId, request.MatchId);

        // Convert command to model using extension method
        var model = request.ToModel();

        try
        {
            // Persist via repository upsert operation
            var result = await _matchLineupRepository.UpsertLineupItemAsync(model, cancellationToken);

            _logger.LogInformation("Match lineup entry {Id} successfully created.", result.Id);
            return result.Id;
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001") // Custom error: Ineligible player
        {
            _logger.LogWarning(ex, "Lineup creation failed: Player {PlayerRosterId} does not belong to match teams.", request.PlayerRosterId);
            throw new ConflictException("The player does not belong to any team participating in this match.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0003") // Custom error: Lineup limit exceeded
        {
            _logger.LogWarning(ex, "Lineup creation failed: Team lineup limit exceeded for Match {MatchId}.", request.MatchId);
            throw new ConflictException(ex.MessageText, ex); // MessageText contains the specific limit exceeded details
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
        {
            _logger.LogWarning(ex, "Lineup creation failed: Player {PlayerRosterId} is already in the lineup for Match {MatchId}.",
                request.PlayerRosterId, request.MatchId);
            throw new ConflictException("This player is already registered in the lineup for this match.", ex);
        }
    }
}