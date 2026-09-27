using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TTA.BusinessLogic.Features.TimeAnchors.Handlers;
using TTA.BusinessLogic.Features.TimeAnchors.Queries;
using TTA.Common.Exceptions;
using TTA.DataAccess.Enums;
using TTA.DataAccess.Models;
using TTA.DataAccess.Repository.Api;

namespace TTA.BusinessLogic.Tests.Features.TimeAnchors.Handlers;

/// <summary>
/// Unit tests for the <see cref="GetTimeAnchorByIdHandler"/> class.
/// Ensures correct mapping of repository results to DTOs, scope validation, and proper error handling.
/// </summary>
public class GetTimeAnchorByIdHandlerTests
{
    private readonly Mock<ITimeAnchorRepository> _timeAnchorRepositoryMock;
    private readonly Mock<ILogger<GetTimeAnchorByIdHandler>> _loggerMock;
    private readonly GetTimeAnchorByIdHandler _handler;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTimeAnchorByIdHandlerTests"/> class.
    /// Sets up mocks and the handler under test.
    /// </summary>
    public GetTimeAnchorByIdHandlerTests()
    {
        _timeAnchorRepositoryMock = new Mock<ITimeAnchorRepository>();
        _loggerMock = new Mock<ILogger<GetTimeAnchorByIdHandler>>();

        _handler = new GetTimeAnchorByIdHandler(
            _timeAnchorRepositoryMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that the handler returns a correctly populated response DTO
    /// when the time anchor exists and belongs to the specified match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ReturnAnchorResponse_When_AnchorExistsAndMatchIdIsCorrect()
    {
        // Arrange
        var anchorId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var query = new GetTimeAnchorByIdQuery(matchId, anchorId);

        var existingAnchor = new TimeAnchor
        {
            Id = anchorId,
            MatchId = matchId,
            PeriodNumber = 2,
            Type = TimeAnchorType.StoppageStart,
            Timestamp = DateTime.UtcNow
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetByIdAsync(anchorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchor);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(anchorId);
        result.MatchId.Should().Be(existingAnchor.MatchId);
        result.PeriodNumber.Should().Be(existingAnchor.PeriodNumber);
        result.Type.Should().Be(existingAnchor.Type);
        result.Timestamp.Should().Be(existingAnchor.Timestamp);

        _timeAnchorRepositoryMock.Verify(r => r.GetByIdAsync(anchorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown 
    /// when the requested time anchor does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_AnchorDoesNotExist()
    {
        // Arrange
        var anchorId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var query = new GetTimeAnchorByIdQuery(matchId, anchorId);

        _timeAnchorRepositoryMock
            .Setup(r => r.GetByIdAsync(anchorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeAnchor?)null);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Time anchor with ID {anchorId} was not found for the specified match.");

        _timeAnchorRepositoryMock.Verify(r => r.GetByIdAsync(anchorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a <see cref="NotFoundException"/> is thrown 
    /// when the anchor exists but belongs to a different match.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Throw_NotFoundException_When_AnchorBelongsToDifferentMatch()
    {
        // Arrange
        var anchorId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var query = new GetTimeAnchorByIdQuery(matchId, anchorId);

        // Setup existing anchor with a completely different MatchId
        var existingAnchor = new TimeAnchor
        {
            Id = anchorId,
            MatchId = Guid.NewGuid(),
            PeriodNumber = 1,
            Type = TimeAnchorType.PeriodStart,
            Timestamp = DateTime.UtcNow
        };

        _timeAnchorRepositoryMock
            .Setup(r => r.GetByIdAsync(anchorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnchor);

        // Act
        var act = async () => await _handler.Handle(query, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Time anchor with ID {anchorId} was not found for the specified match.");
    }
}