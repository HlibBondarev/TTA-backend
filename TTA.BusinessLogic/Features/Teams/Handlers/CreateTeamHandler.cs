using MediatR;
using Microsoft.Extensions.Logging;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Features.Teams.Handlers;

/// <summary>
/// Handles the creation of a new team by persisting it via the repository.
/// </summary>
public class CreateTeamHandler(
    ITeamRepository repository,
    ILogger<CreateTeamHandler> logger) : IRequestHandler<CreateTeamCommand, Guid>
{
    private readonly ITeamRepository _repository = repository;
    private readonly ILogger<CreateTeamHandler> _logger = logger;

    /// <inheritdoc />
    public async Task<Guid> Handle(CreateTeamCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating new team '{TeamName}' for Club {ClubId}.",
            command.Name, command.ClubId);

        var team = command.ToModel();

        var result = await _repository.CreateTeamAsync(team, cancellationToken);

        _logger.LogInformation("Team '{TeamName}' successfully created with ID {TeamId}.",
            result.Name, result.Id);

        return result.Id;
    }
}