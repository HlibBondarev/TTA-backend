using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Tournaments.DTOs;
using TTA.BusinessLogic.Features.Tournaments.Queries;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Tournaments.Handlers;

/// <summary>
/// Handles the retrieval of a tournament by its unique identifier.
/// </summary>
public class GetTournamentByIdHandler(
    ITournamentRepository repository,
    ILogger<GetTournamentByIdHandler> logger) : IRequestHandler<GetTournamentByIdQuery, TournamentResponse?>
{
    private readonly ITournamentRepository _repository = repository;
    private readonly ILogger<GetTournamentByIdHandler> _logger = logger;

    public async Task<TournamentResponse?> Handle(GetTournamentByIdQuery query, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Attempting to retrieve tournament with ID: {TournamentId}", query.Id);

        var result = await _repository.GetByIdAsync(query.Id, cancellationToken);

        if (result == null)
        {
            _logger.LogWarning("Tournament lookup failed: ID {TournamentId} not found.", query.Id);
            return null;
        }

        _logger.LogInformation("Successfully retrieved tournament: {TournamentName}", result.Name);
        return result.ToResponse();
    }
}