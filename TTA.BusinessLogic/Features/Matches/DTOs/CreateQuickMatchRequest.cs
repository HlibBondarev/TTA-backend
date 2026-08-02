namespace TTA.BusinessLogic.Features.Matches.DTOs;

/// <summary>
/// Represents the HTTP request payload for launching a quick match creation process.
/// </summary>
public class CreateQuickMatchRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the target sport discipline.
    /// </summary>
    public Guid SportId { get; set; }

    /// <summary>
    /// Gets or sets the optional unique identifier of the sport configuration.
    /// If omitted, the system automatically falls back to the default configuration of the specified sport.
    /// </summary>
    public Guid? ConfigurationId { get; set; }
}