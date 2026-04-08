using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using System.Reflection;
using TTA.BusinessLogic.Features.Teams.Commands;
using TTA.BusinessLogic.Features.Teams.Handlers;
using TTA.Common.Enums;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.Teams.Handlers;

/// <summary>
/// Unit tests for the <see cref="AddTeamMemberHandler"/>.
/// </summary>
public class AddTeamMemberHandlerTests
{
    private readonly Mock<ITeamMembershipRepository> _membershipRepoMock;
    private readonly Mock<ITeamRepository> _teamRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<ILogger<AddTeamMemberHandler>> _loggerMock;
    private readonly AddTeamMemberHandler _handler;

    public AddTeamMemberHandlerTests()
    {
        _membershipRepoMock = new Mock<ITeamMembershipRepository>();
        _teamRepoMock = new Mock<ITeamRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<AddTeamMemberHandler>>();

        _handler = new AddTeamMemberHandler(
            _membershipRepoMock.Object,
            _teamRepoMock.Object,
            _userRepoMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the target team does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_TeamNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new AddTeamMemberCommand(Guid.NewGuid(), "auth0|user123", TeamRole.AssistantCoach, true);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Team?)null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.Handle(command, CancellationToken.None));

        _membershipRepoMock.Verify(x => x.CreateMembershipWithPolicyAsync(It.IsAny<TeamMembership>(), It.IsAny<AppRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="NotFoundException"/> when the user to be added does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_UserNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var command = new AddTeamMemberCommand(Guid.NewGuid(), "non-existent-user@mail.com", TeamRole.HeadCoach, true);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        _userRepoMock
            .Setup(x => x.GetByEmailAsync(command.UserEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]); // Return empty list for "not found"

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => _handler.Handle(command, CancellationToken.None));
    }

    /// <summary>
    /// Verifies that a valid command results in a new membership being persisted with correct properties.
    /// </summary>
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateMembershipAndReturnId()
    {
        // Arrange
        string userId = "auth0|valid-user";
        var command = new AddTeamMemberCommand(
            TeamId: Guid.NewGuid(),
            UserEmail: "auth0-valid-user@mail.com",
            RoleInTeam: TeamRole.AssistantCoach,
            IsPrimary: false
        );

        var expectedId = Guid.NewGuid();

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        _userRepoMock
            .Setup(x => x.GetByEmailAsync(command.UserEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new User { Id = userId }]);

        _membershipRepoMock
            .Setup(x => x.CreateMembershipWithPolicyAsync(It.IsAny<TeamMembership>(), It.IsAny<AppRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TeamMembership m, AppRole r, CancellationToken _) =>
            {
                m.Id = expectedId;
                return m;
            });

        // Act
        var resultId = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(expectedId, resultId);

        _membershipRepoMock.Verify(x => x.CreateMembershipWithPolicyAsync(
            It.Is<TeamMembership>(m =>
                m.TeamId == command.TeamId &&
                m.UserId == userId &&
                m.RoleInTeam == command.RoleInTeam &&
                m.IsPrimary == command.IsPrimary),
                It.IsAny<AppRole>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Matches the string: "Processing AddTeamMemberCommand for User..."
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Processing AddTeamMemberCommand")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the handler throws an <see cref="InvalidOperationException"/> 
    /// when multiple users are found with the same email.
    /// </summary>
    [Fact]
    public async Task Handle_MultipleUsersFoundWithSameEmail_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var command = new AddTeamMemberCommand(
            TeamId: Guid.NewGuid(),
            UserEmail: "duplicate@mail.com",
            RoleInTeam: TeamRole.Player,
            IsPrimary: true
        );

        // Mocking two users with the same email to simulate data inconsistency
        _userRepoMock
            .Setup(x => x.GetByEmailAsync(command.UserEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new User { Id = "auth0|user-1", Email = "duplicate@mail.com" },
                new User { Id = "auth0|user-2", Email = "duplicate@mail.com" }
            ]);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(command, CancellationToken.None));

        // Verify that an Error was logged before throwing
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Multiple users found")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the handler masks the user's email in the logs to protect PII.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldLogMaskedEmailInsteadOfPlaintext()
    {
        // Arrange
        var plainEmail = "sensitive-user@example.com";
        var maskedPart = "se***@example.com"; // Expected mask for 'sensitive-user'
        var command = new AddTeamMemberCommand(Guid.NewGuid(), plainEmail, TeamRole.Player, true);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        _userRepoMock
            .Setup(x => x.GetByEmailAsync(command.UserEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new User { Id = "auth0|123", Email = plainEmail }]);

        _membershipRepoMock
            .Setup(x => x.CreateMembershipWithPolicyAsync(It.IsAny<TeamMembership>(), It.IsAny<AppRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamMembership { Id = Guid.NewGuid() });

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        // Check that the log message contains the masked version and DOES NOT contain the full plain email
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                    v.ToString()!.Contains(maskedPart) &&
                    !v.ToString()!.Contains(plainEmail)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that the handler throws a <see cref="ConflictException"/> 
    /// when the database reports a duplicate active membership (Postgres Error 23505).
    /// </summary>
    [Fact]
    public async Task Handle_DuplicateActiveRole_ShouldThrowConflictException()
    {
        // Arrange
        var command = new AddTeamMemberCommand(
            TeamId: Guid.NewGuid(),
            UserEmail: "conflict@example.com",
            RoleInTeam: TeamRole.Player,
            IsPrimary: false
        );

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        _userRepoMock
            .Setup(x => x.GetByEmailAsync(command.UserEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new User { Id = "test-user-id" }]);

        // Simulate PostgreSQL Unique Violation (23505)
        var pgException = CreatePostgresException("23505");

        _membershipRepoMock
            .Setup(x => x.CreateMembershipWithPolicyAsync(It.IsAny<TeamMembership>(), It.IsAny<AppRole>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(pgException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ConflictException>(() => _handler.Handle(command, CancellationToken.None));

        // Verify exception details
        Assert.Contains(command.RoleInTeam.ToString(), exception.Message);
        // Verify inner exception is preserved
        Assert.Equal(pgException, exception.InnerException);

        // FIX: Update logger verification to expect the pgException object
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Member Refinement Failed")),
                pgException, // Changed from null to pgException
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the handler wraps unexpected exceptions into an ApplicationException 
    /// with contextual information to satisfy Sonar rule S2139.
    /// </summary>
    [Fact]
    public async Task Handle_UnexpectedException_ShouldRethrowWithContext()
    {
        // Arrange
        var command = new AddTeamMemberCommand(Guid.NewGuid(), "error@example.com", TeamRole.Analyst, false);

        _userRepoMock
            .Setup(x => x.GetByEmailAsync(command.UserEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new User { Id = "user-123", Email = command.UserEmail }]);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        var expectedInnerException = new Exception("Database connection failed");

        _membershipRepoMock
            .Setup(x => x.CreateMembershipWithPolicyAsync(It.IsAny<TeamMembership>(), It.IsAny<AppRole>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedInnerException);

        // Act & Assert
        // 1. Verify that it throws ApplicationException (as defined in our handler)
        var actualException = await Assert.ThrowsAsync<ApplicationException>(() => _handler.Handle(command, CancellationToken.None));

        // 2. Verify contextual message
        Assert.Contains("Error occurred while creating team membership", actualException.Message);
        Assert.Contains("user-123", actualException.Message);

        // 3. Verify inner exception is preserved (Critical for S2630/S2139)
        Assert.Equal(expectedInnerException, actualException.InnerException);

        // NOTE: We no longer verify _loggerMock.LogError here because we removed it 
        // to satisfy Sonar's "Log or Rethrow" rule. Logging is now handled by Global Middleware.
    }

    /// <summary>
    /// Verifies that an undefined TeamRole throws an <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_UndefinedRole_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        // (int)999 is an undefined value for TeamRole enum
        var command = new AddTeamMemberCommand(Guid.NewGuid(), "unknown-role@example.com", (TeamRole)999, false);

        _teamRepoMock
            .Setup(x => x.GetByIdAsync(command.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Team { Id = command.TeamId });

        _userRepoMock
            .Setup(x => x.GetByEmailAsync(command.UserEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new User { Id = "user-123" }]);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _handler.Handle(command, CancellationToken.None));
    }

    /// <summary>
    /// Helper method to create a <see cref="PostgresException"/> with a specific SQL state for testing purposes.
    /// Uses reflection to bypass the lack of a public constructor in Npgsql.
    /// </summary>
    /// <param name="sqlState">The 5-character SQLState code (e.g., "23505").</param>
    /// <returns>A populated instance of PostgresException.</returns>
    private static PostgresException CreatePostgresException(string sqlState)
    {
        var type = typeof(PostgresException);

        // Get the internal constructor that accepts the most parameters
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (constructor == null)
        {
            throw new InvalidOperationException("Failed to locate a suitable constructor for PostgresException.");
        }

        var parameters = constructor.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var paramName = parameters[i].Name?.ToLower();

            // Assign the required SQL state, others get dummy test values
            if (paramName == "sqlstate") args[i] = sqlState;
            else if (parameters[i].ParameterType == typeof(string)) args[i] = "Database integrity constraint violation";
            else args[i] = null;
        }

        return (PostgresException)constructor.Invoke(args);
    }
}