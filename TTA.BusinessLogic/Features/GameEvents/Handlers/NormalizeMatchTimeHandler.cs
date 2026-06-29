using MediatR;
using Microsoft.Extensions.Logging;
using Npgsql;
using TTA.BusinessLogic.Features.GameEvents.Commands;
using TTA.Common.Exceptions;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.GameEvents.Handlers;

/// <summary>
/// Handles the execution pipeline for the <see cref="NormalizeMatchTimeCommand"/>.
/// Coordinates batch execution with detailed telemetry logging and maps infrastructure exceptions to domain rules.
/// </summary>
/// <param name="gameEventRepository">The repository abstraction used to execute game event data operations.</param>
/// <param name="logger">The logger instance for tracking execution flow telemetry.</param>
public class NormalizeMatchTimeHandler(
    IGameEventRepository gameEventRepository,
    ILogger<NormalizeMatchTimeHandler> logger) : IRequestHandler<NormalizeMatchTimeCommand>
{
    private readonly IGameEventRepository _gameEventRepository = gameEventRepository;
    private readonly ILogger<NormalizeMatchTimeHandler> _logger = logger;

    /// <summary>
    /// Processes the batch normalization request by invoking the optimized PostgreSQL storage routine.
    /// </summary>
    /// <param name="request">The incoming command payload containing match and team identifiers.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests during processing.</param>
    /// <returns>A task representing the completed asynchronous operation.</returns>
    public async Task Handle(NormalizeMatchTimeCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting batch match time normalization for Match {MatchId} and Team {TeamId}.",
            request.MatchId, request.TeamId);

        try
        {
            // Delegate the high-performance batch operation to the repository layer
            await _gameEventRepository.NormalizeMatchEventsTimeAsync(
                request.MatchId,
                request.TeamId,
                cancellationToken);

            _logger.LogInformation("Batch match time normalization successfully completed for Match {MatchId}, Team {TeamId}.",
                request.MatchId, request.TeamId);
        }
        catch (PostgresException ex) when (ex.SqlState == "P0001")
        {
            _logger.LogWarning(ex, "Batch match time normalization failed due to database business rule for Match {MatchId}: {Message}",
                request.MatchId, ex.MessageText);

            throw new ConflictException(ex.MessageText, ex);
        }
    }
}