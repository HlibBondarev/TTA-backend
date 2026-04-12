using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.ComponentModel.DataAnnotations;
using TTA.BusinessLogic.Features.Tournaments.Commands;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.Common.Exceptions;
using TTA.Common.Extensions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Tournaments.Handlers;

/// <summary>
/// Handles the update of a tournament record, 
/// ensuring data integrity and mapping the result to a response DTO.
/// </summary>
public class UpdateTournamentHandler(
    ITournamentRepository repository,
    ILogger<UpdateTournamentHandler> logger) : IRequestHandler<UpdateTournamentCommand, TournamentResponse>
{
    private readonly ITournamentRepository _repository = repository;
    private readonly ILogger<UpdateTournamentHandler> _logger = logger;

    /// <summary>
    /// Processes the tournament command by mapping it to a domain model and persisting it via the repository.
    /// After successful persistence, the result is mapped to a <see cref="TournamentResponse"/>.
    /// </summary>
    /// <param name="command">The command containing tournament details such as name, dates, and associations.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for request cancellation.</param>
    /// <returns>A <see cref="TournamentResponse"/> containing the persisted tournament data.</returns>
    /// <exception cref="ValidationException">Thrown when the tournament ID is missing.</exception>
    /// <exception cref="ForbiddenException">Thrown when the user is not the owner or lacks permissions.</exception>
    /// <exception cref="NotFoundException">Thrown when the tournament or related entities do not exist.</exception>
    /// <exception cref="ConflictException">Thrown when a database constraint (like date range) is violated.</exception>
    public async Task<TournamentResponse> Handle(UpdateTournamentCommand command, CancellationToken cancellationToken)
    {
        // 1. Validate OwnerId for Authorization
        if (string.IsNullOrEmpty(command.OwnerId))
        {
            _logger.LogWarning("Update rejected: OwnerId is missing for tournament {TournamentId}", command.Id);
            throw new ForbiddenException("You do not have permission to update this tournament.");
        }

        // 2. Log attempt
        _logger.LogInformation("Processing tournament update: {TournamentName} (ID: {TournamentId})",
            command.Name, command.Id);

        // 3. Fetch existing tournament to verify actual ownership
        var existingTournament = await _repository.GetByIdAsync(command.Id, cancellationToken);

        if (existingTournament == null)
        {
            _logger.LogWarning("Tournament not found: {TournamentId}", command.Id);
            throw new NotFoundException("Tournament not found.");
        }

        if (existingTournament.OwnerId != command.OwnerId)
        {
            _logger.LogWarning("Unauthorized update attempt by {UserId} for tournament {TournamentId}",
                command.OwnerId.MaskAuthId(), command.Id);
            throw new ForbiddenException("You do not have permission to update this tournament.");
        }

        // 4. Map and persist
        var tournament = command.ToModel();

        try
        {
            var result = await _repository.CreateOrUpdate(tournament, cancellationToken);
            _logger.LogInformation("Successfully persisted tournament {TournamentId}", result.Id);
            return result.ToResponse();
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Ownership validation failed in DB for tournament {Id}", command.Id);
            throw new ForbiddenException("You do not have permission to update this tournament.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "22023") // Invalid Parameter Value
        {
            _logger.LogWarning(ex, "Invalid date range for {TournamentName}", command.Name);
            throw new ConflictException("The tournament start date must be before the end date.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign Key Violation
        {
            _logger.LogWarning(ex, "Related entity not found for {TournamentName}", command.Name);
            throw new NotFoundException("One or more related entities (City, Sport, or Configuration) do not exist.", ex);
        }
    }
}