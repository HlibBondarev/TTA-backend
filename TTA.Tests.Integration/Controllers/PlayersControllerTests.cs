using TTA.Tests.Integration.Infrastructure;

namespace TTA.Tests.Integration.Controllers;

/// <summary>
/// Integration tests for the PlayersController using the real API stack and database container.
/// </summary>
public class PlayersControllerTests(DatabaseFixture fixture) : BaseApiTest(fixture)
{
}