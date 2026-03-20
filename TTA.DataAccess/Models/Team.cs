using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class Team : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid ClubId { get; set; }
    public string Name { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
