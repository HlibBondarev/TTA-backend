using TTA.DataAccess.Enums;
using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class TeamMembership : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public Guid TeamId { get; set; }
    public TeamRole RoleInTeam { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
    public bool IsPrimary { get; set; }
}