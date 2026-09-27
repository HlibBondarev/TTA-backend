namespace TTA.BusinessLogic.Features.Sports.DTOs;

/// <summary>
/// Represents summary details of a sport.
/// </summary>
/// <param name="Id">The unique identifier of the sport.</param>
/// <param name="Name">The full name of the sport.</param>
/// <param name="ShortName">The abbreviated name of the sport.</param>
/// <param name="DefaultConfigId">The default configuration identifier associated with the sport.</param>
public record SportResponse(
    Guid Id,
    string Name,
    string ShortName,
    Guid DefaultConfigId);