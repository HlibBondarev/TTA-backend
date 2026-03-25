using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TTA.WebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController(ILogger<WeatherForecastController> logger) : ControllerBase
    {
        private readonly ILogger<WeatherForecastController> _logger = logger;

        [Authorize]
        [HttpGet]
        [ProducesResponseType(typeof(WeatherForecastResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult Get()
        {
            _logger.LogInformation("Start  Get action in {WeatherForecastController}.",
                typeof(WeatherForecastController).Name);

            // Our existing fallback logic for auth0Id
            var auth0Id = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value;

            _logger.LogInformation("Get Auth0Id = {Auth0Id}.", auth0Id);

            // Returning a typed record instead of an anonymous object
            return Ok(new WeatherForecastResponse("Success", auth0Id));
        }
    }
}
