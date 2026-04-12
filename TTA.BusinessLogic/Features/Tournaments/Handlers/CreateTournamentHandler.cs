using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.Tournaments.Commands;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.Common.Exceptions;
using TTA.Common.Extensions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Tournaments.Handlers;

/// <summary>
/// Handles the creation of a tournament record, 
/// ensuring data integrity and mapping the result to a response DTO.
/// </summary>
public class CreateTournamentHandler(
    ITournamentRepository repository,
    ILogger<CreateTournamentHandler> logger) : IRequestHandler<CreateTournamentCommand, TournamentResponse>
{
    private readonly ITournamentRepository _repository = repository;
    private readonly ILogger<CreateTournamentHandler> _logger = logger;

    /// <summary>
    /// Processes the tournament creation command.
    /// Maps the command to a domain model and persists it via the repository.
    /// </summary>
    /// <param name="command">The command containing tournament details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TournamentResponse"/> containing the persisted data.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when OwnerId is missing.</exception>
    /// <exception cref="ConflictException">Thrown when business rules (e.g., date range) are violated.</exception>
    /// <exception cref="NotFoundException">Thrown when related entities (City, Sport, etc.) do not exist.</exception>
    public async Task<TournamentResponse> Handle(CreateTournamentCommand command, CancellationToken cancellationToken)
    {
        // 1. Validate OwnerId existence (Consistent with Update Handler)
        if (string.IsNullOrEmpty(command.OwnerId))
        {
            _logger.LogWarning("Creation rejected: OwnerId is missing.");
            throw new UnauthorizedAccessException("User identification is required to create a tournament.");
        }

        // 2. Log the creation attempt with masked PII
        _logger.LogInformation("Processing creation for tournament: {TournamentName} by owner {OwnerId}",
            command.Name, command.OwnerId.MaskAuthId());

        // 3. Map to domain model
        var tournament = command.ToModel();

        try
        {
            // 4. Persistence via repository
            var result = await _repository.CreateOrUpdate(tournament, cancellationToken);

            _logger.LogInformation("Successfully created tournament {TournamentId}", result.Id);

            return result.ToResponse();
        }
        catch (PostgresException ex) when (ex.SqlState == "22023") // Invalid Parameter Value
        {
            _logger.LogWarning(ex, "Tournament creation failed: Invalid date range for {TournamentName}", command.Name);
            throw new ConflictException("The tournament start date must be before the end date.", ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign Key Violation
        {
            _logger.LogWarning(ex, "Tournament creation failed: Related entity not found for {TournamentName}", command.Name);
            throw new NotFoundException("One or more related entities (City, Sport, or Configuration) do not exist.", ex);
        }
    }
}