using TTA.DataAccess.Enums;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Team : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid ClubId { get; set; }
    public Guid SportId { get; set; } // Added for multi-sport support
    public string Name { get; set; } = null!;
    // Earliest birth year allowed for youth teams. Null for Senior teams.
    public int? MinBirthYear { get; set; }
    // Team category: Male or Female (stored as string/enum)
    public Gender Gender { get; set; }
    public DateTime CreatedAt { get; set; }
}
