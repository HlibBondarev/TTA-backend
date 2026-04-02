using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TTA.WebAPI.Controllers;

/// <summary>
/// Controller for managing player (athlete) operations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PlayersController(
    IMediator mediator,
    ILogger<PlayersController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger _logger = logger;
}