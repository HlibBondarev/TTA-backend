using MediatR;
using TTA.BusinessLogic.Features.Matches.DTOs;

namespace TTA.BusinessLogic.Features.Matches.Commands;

/// <summary>
/// Command to initiate JIT infrastructure provisioning and quick match creation.
/// Encapsulates quick match request details and authenticated user claims.
/// </summary>
/// <param name="Request">The request payload containing client-generated match ID, sport ID, mandatory configuration ID, and guest team tracking flag.</param>
/// <param name="UserId">The unique identifier of the authenticated user from Auth0 claims.</param>
/// <param name="UserEmail">The email of the authenticated user from Auth0 claims.</param>
/// <param name="UserName">The display name of the authenticated user from Auth0 claims.</param>
public record CreateQuickMatchCommand(
    CreateQuickMatchRequest Request,
    string UserId,
    string UserEmail,
    string UserName
) : IRequest<QuickMatchResponse>;