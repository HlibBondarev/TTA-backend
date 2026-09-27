using System.ComponentModel.DataAnnotations.Schema;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

/// <summary>
/// Represents a tournament entity within the system.
/// A tournament is a competitive event for a specific sport held in a particular city, 
/// managed by an owner and governed by a specific configuration.
/// </summary>
public class Tournament : IKeyedEntity<Guid>
{
    /// <summary>
    /// Gets or sets the unique identifier for the tournament.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the sport associated with this tournament.
    /// Combined with <see cref="ConfigurationId"/>, it ensures data integrity.
    /// </summary>
    public Guid SportId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the configuration/ruleset for this tournament.
    /// Must belong to the specified <see cref="SportId"/>.
    /// </summary>
    public Guid ConfigurationId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the city where the tournament takes place.
    /// </summary>
    public Guid CityId { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the user who created and owns this tournament.
    /// Only this user has permission to update the tournament details.
    /// </summary>
    [Column("ownerid")]
    public string OwnerId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the name of the tournament.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the date and time when the tournament starts.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the tournament ends.
    /// Must be greater than or equal to <see cref="StartDate"/>.
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the tournament record was created.
    /// This value is typically set once upon creation.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}