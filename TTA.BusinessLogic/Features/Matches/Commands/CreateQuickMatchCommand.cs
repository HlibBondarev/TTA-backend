using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Represents a MediatR command to provision and create a quick match instance.
/// </summary>
/// <param name="Request">The quick match creation request payload.</param>
/// <param name="UserId">The authenticated user identifier executing the action.</param>
public record CreateQuickMatchCommand(
    CreateQuickMatchRequest Request,
    string UserId
) : IRequest<QuickMatchResponse>;